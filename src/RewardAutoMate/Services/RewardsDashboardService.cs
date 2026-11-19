using System.Collections.ObjectModel;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using RewardAutoMate.Configuration;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// Microsoft Rewards Dashboard 交互服务
/// </summary>
public class RewardsDashboardService
{
    private readonly EdgeDriver Driver;
    private int TotalPointsEarned = 0;

    /// <summary>
    /// Dashboard 所在的主窗口句柄，Daily Set 任务在新标签页打开，完成后要切回这个句柄
    /// </summary>
    private string MainWindowHandle = "";

    /// <summary>
    /// Daily Set 卡片选择器，按优先级依次尝试
    /// ! 实测（2026-11-17）首页结构为 &lt;section id="dailyset"&gt; 内 3 个 target=_blank 的 a[href]
    /// </summary>
    private static readonly string[] DailySetCardSelectors =
    [
        "#dailyset a[href]",
        "[data-bi-name*='DailySet']",
        ".reward-card, .offer-card, .mee-card",
        "[class*='Card'], [class*='card']"
    ];

    public RewardsDashboardService(EdgeDriver _driver)
    {
        Driver = _driver;
    }

    /// <summary>
    /// 获取当前获得积分
    /// </summary>
    public int GetTotalPoints() => TotalPointsEarned;

    /// <summary>
    /// 导航到 Dashboard 并等待加载
    /// </summary>
    public void NavigateToDashboard()
    {
        AppLogger.Info($"正在导航到 Dashboard: {AppConfig.DashboardUrl}");
        Driver.Navigate().GoToUrl(AppConfig.DashboardUrl);
        WaitForPageLoad();
        string _title = Driver.Title;
        AppLogger.Info($"Dashboard 页面已加载，标题: {_title}");
        CheckLoginStatus();
    }

    /// <summary>
    /// 检查当前页面是否被重定向到登录页（未登录），若是则记录警告
    /// </summary>
    private void CheckLoginStatus()
    {
        try
        {
            string _url = Driver.Url;
            if (_url.Contains("login.live.com")
                || _url.Contains("login.microsoftonline")
                || _url.Contains("signin")
                || _url.Contains("login"))
            {
                AppLogger.Warning("检测到已跳转到登录页，当前 EdgeUserData 可能未登录。请先以 Headless=false 运行一次完成账户登录后再开启无头运行。");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"检查登录状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取当前账户积分和会员状态（基于真实页面结构）
    /// </summary>
    public DashboardStatus GetDashboardStatus()
    {
        try
        {
            int _points = 0;
            string _level_text = "未知";

            // ! 读取右上角积分 - 遍历 header 中的 p 标签
            try
            {
                ReadOnlyCollection<IWebElement> _header_ps = Driver.FindElements(By.CssSelector("header p"));
                foreach (IWebElement _p in _header_ps)
                {
                    string _text = _p.Text.Trim().Replace(",", "");
                    if (int.TryParse(_text, out int _val) && _val > 1000)
                    {
                        _points = _val;
                        AppLogger.Info($"读取到账户积分: {_points:N0}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"读取右上角积分失败: {ex.Message}");
            }

            // ? 如果 header 没找到，尝试页面中所有纯数字段落
            if (_points == 0)
            {
                try
                {
                    ReadOnlyCollection<IWebElement> _all_ps = Driver.FindElements(By.CssSelector("p"));
                    foreach (IWebElement _p in _all_ps)
                    {
                        string _text = _p.Text.Trim().Replace(",", "");
                        if (int.TryParse(_text, out int _val) && _val > 1000)
                        {
                            _points = _val;
                            AppLogger.Info($"从页面段落读取到积分: {_points:N0}");
                            break;
                        }
                    }
                }
                catch { }
            }

            // ! 读取会员等级 - 查找包含"会员"的文本
            try
            {
                ReadOnlyCollection<IWebElement> _all_elements = Driver.FindElements(By.XPath("//*[contains(text(),'会员')]"));
                foreach (IWebElement _el in _all_elements)
                {
                    string _text = _el.Text.Trim();
                    if (_text.Contains("银牌") || _text.Contains("金牌") || _text.Contains("白金"))
                    {
                        _level_text = _text;
                        AppLogger.Info($"读取到会员等级: {_level_text}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"读取会员等级失败: {ex.Message}");
            }

            // ! 读取会员进度 - 还需积分 / 还需完成活动
            try
            {
                ReadOnlyCollection<IWebElement> _progress_elements = Driver.FindElements(By.XPath("//*[contains(text(),'还需积分')]"));
                foreach (IWebElement _el in _progress_elements)
                {
                    string _text = _el.Text.Trim();
                    AppLogger.Info($"读取到会员进度: {_text}");
                    break;
                }
            }
            catch { }

            return new DashboardStatus
            {
                CurrentPoints = _points,
                Level = _level_text,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("读取 Dashboard 状态失败", ex);
            return new DashboardStatus { CurrentPoints = 0, Level = "未知", Timestamp = DateTime.Now };
        }
    }

    /// <summary>
    /// 读取今日进度（搜索、活动、签到等）
    /// </summary>
    public TodayProgress GetTodayProgress()
    {
        TodayProgress _progress = new TodayProgress();

        try
        {
            AppLogger.Info("正在读取今日进度...");

            // ! 读取进度按钮（搜索、活动、签到等）
            ReadOnlyCollection<IWebElement> _buttons = Driver.FindElements(By.CssSelector("button"));
            foreach (IWebElement _btn in _buttons)
            {
                string _text = _btn.Text.Trim();

                // ? 搜索进度: "必应搜索: x/x"
                if (_text.Contains("搜索") && _text.Contains("/"))
                {
                    _progress.SearchProgress = ParseFraction(_text);
                    AppLogger.Info($"搜索进度: {_text}");
                }
                // ? 活动进度: "每日活动 活动: x/x"
                else if (_text.Contains("活动") && _text.Contains("/"))
                {
                    _progress.ActivityProgress = ParseFraction(_text);
                    AppLogger.Info($"活动进度: {_text}");
                }
                // ? 签到进度: "移动应用 签到: x/x"
                else if (_text.Contains("签到") && _text.Contains("/"))
                {
                    _progress.CheckInProgress = ParseFraction(_text);
                    AppLogger.Info($"签到进度: {_text}");
                }
            }

            // ! 读取可领取数量
            try
            {
                ReadOnlyCollection<IWebElement> _all_elements = Driver.FindElements(By.XPath("//*"));
                foreach (IWebElement _el in _all_elements)
                {
                    string _text = _el.Text.Trim();
                    if (_text == "可领取")
                    {
                        try
                        {
                            IWebElement _parent = _el.FindElement(By.XPath(".."));
                            string _parent_text = _parent.Text.Trim();
                            string[] _lines = _parent_text.Split('\n');
                            foreach (string _line in _lines)
                            {
                                string _num = _line.Trim();
                                if (int.TryParse(_num, out int _val) && _val >= 0 && _val < 100)
                                {
                                    _progress.ClaimableCount = _val;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"读取可领取数量失败: {ex.Message}");
            }

            // ! 统计 Daily Set 完成数
            try
            {
                _progress.DailySetDone = CountDailySetCompleted();
                _progress.DailySetTotal = 3; // Daily Set 固定 3 项
                AppLogger.Info($"Daily Set 统计: {_progress.DailySetDone}/{_progress.DailySetTotal} 项已完成");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"统计 Daily Set 失败: {ex.Message}");
                _progress.DailySetDone = 0;
                _progress.DailySetTotal = 3;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("读取今日进度时发生异常", ex);
        }

        return _progress;
    }

    private static (int Done, int Total) ParseFraction(string _text)
    {
        try
        {
            // ? 从文本中提取 x/x 格式
            System.Text.RegularExpressions.Match _match =
                System.Text.RegularExpressions.Regex.Match(_text, @"(\d+)\s*/\s*(\d+)");
            if (_match.Success)
            {
                int _done = int.Parse(_match.Groups[1].Value);
                int _total = int.Parse(_match.Groups[2].Value);
                return (_done, _total);
            }
        }
        catch { }
        return (0, 0);
    }

    /// <summary>
    /// 自动领取福利区域积分
    /// </summary>
    public int ClaimBenefits()
    {
        if (!AppConfig.AutoClaimBenefits)
        {
            return 0;
        }

        int _claimed = 0;
        try
        {
            AppLogger.Info("正在扫描福利区域...");
            ReadOnlyCollection<IWebElement> _buttons = Driver.FindElements(By.CssSelector("button, a"));
            AppLogger.Info($"扫描到 {_buttons.Count} 个按钮/链接，正在筛选福利...");

            foreach (IWebElement _btn in _buttons)
            {
                string _text = _btn.Text.ToLower();
                if (_text.Contains("领取") || _text.Contains("claim") || _text.Contains("获取"))
                {
                    try
                    {
                        _btn.Click();
                        _claimed++;
                        AppLogger.Info($"已领取福利: {_btn.Text.Trim()}");
                        Task.Delay(Random.Shared.Next(500, 1500)).Wait();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"领取福利失败: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("自动领取福利时发生异常", ex);
        }

        TotalPointsEarned += _claimed * 10;
        return _claimed;
    }

    /// <summary>
    /// 执行 Daily Set 任务
    /// ! 复刻人工操作：Ctrl+点击在新标签页打开卡片、停留一会儿、关掉，再回 dashboard 复查是否真的入账
    /// </summary>
    public async Task<int> ExecuteDailySet()
    {
        if (!AppConfig.AutoDailySet)
        {
            return 0;
        }

        int _before = 0;
        int _done = 0;
        try
        {
            NavigateToDashboard();
            MainWindowHandle = Driver.CurrentWindowHandle;
            AppLogger.Info($"[DailySet] 主窗口句柄: {ShortHandle(MainWindowHandle)}，当前标签页数: {Driver.WindowHandles.Count}");
            AppLogger.Info($"[DailySet] 当前页面: {Driver.Url} / {Driver.Title}");
            _before = CountDailySetCompleted();
            _done = _before;
            AppLogger.Info($"Daily Set 当前已完成: {_before}/3");

            // ? 每次执行都存一份 dashboard HTML，便于事后核对卡片结构
            DumpPage("dailyset_start");

            // ! 同一条任务偶尔要开两次才结算，所以最多补做两轮
            const int _max_passes = 2;
            for (int _pass = 1; _pass <= _max_passes && _done < 3; _pass++)
            {
                List<(IWebElement Card, string Name, string Url)> _tasks = ScanPendingDailySetTasks();
                if (_tasks.Count == 0)
                {
                    AppLogger.Info("Daily Set 没有待完成的任务");
                    // ? 扫不到任务时把页面存下来，方便事后核对选择器
                    DumpPage($"dailyset_pass{_pass}_empty");
                    break;
                }

                AppLogger.Info($"Daily Set 第 {_pass}/{_max_passes} 轮，待完成 {_tasks.Count} 项");
                int _index = 0;
                foreach ((IWebElement _card, string _name, string _url) in _tasks)
                {
                    _index++;
                    try
                    {
                        AppLogger.Info($"[DailySet] ({_index}/{_tasks.Count}) 开始执行: {_name}");
                        AppLogger.Info($"[DailySet] ({_index}/{_tasks.Count}) 目标链接: {(_url.Length > 0 ? _url : "(无 href)")}");

                        // ! 实测：真实 Ctrl+点击（带 referrer）才能结算，直接 GoToUrl 不行
                        if (!CtrlClickInNewTab(_card) && _url.Length > 0)
                        {
                            AppLogger.Info($"[DailySet] ({_index}/{_tasks.Count}) Ctrl+点击没有开出新标签页，退回直接导航");
                            OpenTaskInNewTab(_url);
                        }

                        // ! 停留太短不结算，实测 35 秒可以，取 25-40 秒随机
                        int _dwell = Random.Shared.Next(25000, 40000);
                        AppLogger.Info($"[DailySet] ({_index}/{_tasks.Count}) 停留 {_dwell / 1000.0:0.0}s 等待结算，当前标签页数: {Driver.WindowHandles.Count}");
                        await Task.Delay(_dwell);
                        AppLogger.Info($"[DailySet] ({_index}/{_tasks.Count}) 停留结束，开始关闭任务标签页");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"  执行 Daily Set 单项任务失败: {ex.Message}");
                    }
                    finally
                    {
                        CloseTaskTabs();
                    }
                }

                // ! 必须重新读页面：「已完成」是服务端结算后才渲染的，用旧元素判断不出来
                NavigateToDashboard();
                _done = CountDailySetCompleted();
                AppLogger.Info($"第 {_pass} 轮后 Daily Set 完成: {_done}/3（本轮新增 {Math.Max(_done - _before, 0)}）");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("执行 Daily Set 时发生异常", ex);
        }

        int _gained = Math.Max(_done - _before, 0);
        TotalPointsEarned += _gained * 10;
        AppLogger.Info($"Daily Set 本次新增完成: {_gained} 项（今日累计 {_done}/3）");
        return _gained;
    }

    /// <summary>
    /// 统计 dashboard 上已完成的 Daily Set 卡片数
    /// </summary>
    private int CountDailySetCompleted()
    {
        try
        {
            int _daily_completed = 0;

            // 方法0：直接读「每日活动」进度按钮，实测页面文本形如 "每日活动\n活动: 3/3"
            ReadOnlyCollection<IWebElement> _buttons = Driver.FindElements(By.CssSelector("button"));
            foreach (IWebElement _btn in _buttons)
            {
                string _text = _btn.Text.Trim();
                if (!_text.Contains("每日活动")) continue;

                (int _done, int _total) = ParseFraction(_text);
                if (_total > 0)
                {
                    AppLogger.Debug($"[DailySet] 计数(方法0) 从进度按钮读取: {Preview(_text, 40)}");
                    return Math.Min(_done, 3);
                }
            }

            // 方法1：遍历所有 p 标签，查找同时包含 "已完成" 和 "+10" 的
            ReadOnlyCollection<IWebElement> _all_ps = Driver.FindElements(By.CssSelector("p"));
            foreach (IWebElement _p in _all_ps)
            {
                string _text = _p.Text.Trim();
                if (_text.Contains("已完成") && _text.Contains("+10"))
                {
                    _daily_completed++;
                    AppLogger.Debug($"[DailySet] 计数(方法1) 命中: {Preview(_text, 40)}");
                }
            }
            AppLogger.Debug($"[DailySet] 计数方法1（p 同时含\"已完成\"和\"+10\"）: {_daily_completed}，p 标签总数: {_all_ps.Count}");

            // 方法2：如果没找到，可能是 "+10" 和 "已完成" 在兄弟元素中
            // 查找所有包含 "+10" 的元素，检查其父容器是否包含"已完成"
            if (_daily_completed == 0)
            {
                ReadOnlyCollection<IWebElement> _plus_elements = Driver.FindElements(By.XPath("//*[contains(text(),'+10')]"));
                HashSet<string> _counted_parents = new HashSet<string>();
                foreach (IWebElement _el in _plus_elements)
                {
                    try
                    {
                        // 向上查找 3 层父元素，检查是否包含"已完成"
                        IWebElement? _parent = _el;
                        for (int i = 0; i < 3 && _parent != null; i++)
                        {
                            string _parent_text = _parent.Text.Trim();
                            if (_parent_text.Contains("已完成") && _parent_text.Length < 300)
                            {
                                // 使用父元素的 TagName + 部分文本作为唯一标识
                                string _key = _parent.TagName + "_" + _parent_text[..Math.Min(50, _parent_text.Length)];
                                if (!_counted_parents.Contains(_key))
                                {
                                    _counted_parents.Add(_key);
                                    _daily_completed++;
                                }
                                break;
                            }
                            _parent = _parent.FindElement(By.XPath(".."));
                        }
                    }
                    catch { }
                }
                AppLogger.Debug($"[DailySet] 计数方法2（\"+10\" 元素向上找 3 层父级含\"已完成\"）: {_daily_completed}，\"+10\" 元素总数: {_plus_elements.Count}");
            }

            // 方法3：遍历所有包含"已完成"的独立元素，限制文本长度避免父容器重复
            if (_daily_completed == 0)
            {
                ReadOnlyCollection<IWebElement> _done_elements = Driver.FindElements(By.XPath("//*[contains(text(),'已完成')]"));
                foreach (IWebElement _el in _done_elements)
                {
                    string _text = _el.Text.Trim();
                    // Daily Set 卡片文本较短；福利区卡片会包含大量文本如"必应 Star 奖励"
                    if (_text.Length < 80 && _text.Contains("+10"))
                    {
                        _daily_completed++;
                        AppLogger.Debug($"[DailySet] 计数(方法3) 命中: {Preview(_text, 40)}");
                    }
                }
                AppLogger.Debug($"[DailySet] 计数方法3（短文本独立元素含\"已完成\"+\"+10\"）: {_daily_completed}，含\"已完成\"元素总数: {_done_elements.Count}");
            }

            return Math.Min(_daily_completed, 3);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"统计 Daily Set 完成数失败: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 扫描待完成的 Daily Set 卡片，返回卡片元素、任务名与任务链接
    /// </summary>
    private List<(IWebElement Card, string Name, string Url)> ScanPendingDailySetTasks()
    {
        List<(IWebElement Card, string Name, string Url)> _result = [];
        HashSet<string> _seen_keys = [];

        IList<IWebElement> _cards = FindDailySetCards(out string _selector);
        AppLogger.Info($"[DailySet] 扫描到 {_cards.Count} 个候选卡片（选择器 \"{_selector}\"）");

        int _index = 0;
        foreach (IWebElement _card in _cards)
        {
            _index++;
            try
            {
                string _text = _card.Text;

                if (string.IsNullOrWhiteSpace(_text))
                {
                    AppLogger.Debug($"[DailySet]   卡片[{_index}] 文本为空，跳过");
                    continue;
                }

                // ! 卡片文本过长的说明选中了外层容器，直接跳过
                if (_text.Length > 400)
                {
                    AppLogger.Debug($"[DailySet]   卡片[{_index}] 文本长度 {_text.Length} 超过 400，判定为外层容器，跳过: {Preview(_text, 40)}");
                    continue;
                }

                if (_text.Contains("已完成") || _text.Contains("completed"))
                {
                    AppLogger.Debug($"[DailySet]   卡片[{_index}] 已完成，跳过: {Preview(_text, 40)}");
                    continue;
                }

                string _name = _text.Split('\n')[0].Trim();
                if (_name.Length == 0)
                {
                    AppLogger.Debug($"[DailySet]   卡片[{_index}] 标题为空，跳过");
                    continue;
                }

                string _url = ResolveTaskUrl(_card);

                // ? 用链接或任务名去重，避免嵌套容器被重复扫描
                string _key = _url.Length > 0 ? _url : _name;
                if (!_seen_keys.Add(_key))
                {
                    AppLogger.Debug($"[DailySet]   卡片[{_index}] 与前面的卡片重复，跳过: {Preview(_name, 30)}");
                    continue;
                }

                AppLogger.Debug($"[DailySet]   卡片[{_index}] 待完成: {Preview(_name, 30)} | 链接: {(_url.Length > 0 ? _url : "(无)")}");
                _result.Add((_card, _name, _url));
            }
            catch (StaleElementReferenceException)
            {
                // ? 页面刷新导致元素失效，下一轮扫描会重新拿到
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"检查卡片失败: {ex.Message}");
            }
        }

        return _result;
    }

    /// <summary>
    /// 按选择器优先级查找 Daily Set 卡片
    /// </summary>
    private IList<IWebElement> FindDailySetCards(out string _used_selector)
    {
        foreach (string _selector in DailySetCardSelectors)
        {
            try
            {
                IList<IWebElement> _cards = Driver.FindElements(By.CssSelector(_selector));
                AppLogger.Debug($"[DailySet] 选择器 \"{_selector}\" 命中 {_cards.Count} 个元素");
                if (_cards.Count > 0)
                {
                    AppLogger.Info($"[DailySet] 采用选择器 \"{_selector}\"，卡片数 {_cards.Count}");
                    _used_selector = _selector;
                    return _cards;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"[DailySet] 选择器 \"{_selector}\" 查询失败: {ex.Message}");
            }
        }

        AppLogger.Warning($"[DailySet] 所有卡片选择器均未命中: {string.Join(" | ", DailySetCardSelectors)}");
        _used_selector = "(none)";
        return [];
    }

    /// <summary>
    /// 取卡片对应的任务链接，取不到返回空串
    /// </summary>
    private string ResolveTaskUrl(IWebElement _card)
    {
        try
        {
            ReadOnlyCollection<IWebElement> _links = _card.FindElements(By.CssSelector("a[href]"));
            foreach (IWebElement _link in _links)
            {
                string _href = (_link.GetAttribute("href") ?? "").Trim();
                if (_href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return _href;
                }
                AppLogger.Debug($"[DailySet]   卡片内 a[href] 不是 http 开头，忽略: {Preview(_href, 60)}");
            }

            // ? 卡片本身可能就是一个链接
            string _own = (_card.GetAttribute("href") ?? "").Trim();
            if (_own.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return _own;
            }

            AppLogger.Debug($"[DailySet]   卡片内未找到可用链接（a[href] 数量: {_links.Count}）");
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"[DailySet]   解析卡片链接失败: {ex.Message}");
        }

        return "";
    }

    /// <summary>
    /// 在新标签页打开任务链接，dashboard 保持在原标签页
    /// </summary>
    private void OpenTaskInNewTab(string _url)
    {
        int _before = Driver.WindowHandles.Count;
        Driver.SwitchTo().NewWindow(WindowType.Tab);
        Driver.Navigate().GoToUrl(_url);
        WaitForPageLoad();
        AppLogger.Info($"[DailySet]   已在新标签页打开（标签页数 {_before} -> {Driver.WindowHandles.Count}）");
        AppLogger.Info($"[DailySet]   任务页: {Driver.Url} / {Driver.Title}");
    }

    /// <summary>
    /// 用 Ctrl+点击在卡片上打开新标签页（等价于人工操作，保留 referrer）
    /// </summary>
    /// <returns>是否真的开出了新标签页</returns>
    private bool CtrlClickInNewTab(IWebElement _card)
    {
        int _before = 0;
        try
        {
            _before = Driver.WindowHandles.Count;
            new Actions(Driver)
                .KeyDown(OpenQA.Selenium.Keys.Control)
                .Click(_card)
                .KeyUp(OpenQA.Selenium.Keys.Control)
                .Perform();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[DailySet]   Ctrl+点击失败: {ex.Message}");
            return false;
        }

        // ? 新标签页不是瞬间出现的，最多等 10 秒
        DateTime _deadline = DateTime.Now.AddSeconds(10);
        while (DateTime.Now < _deadline)
        {
            try
            {
                if (Driver.WindowHandles.Count > _before) break;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[DailySet]   读取标签页失败: {ex.Message}");
                return false;
            }
            Thread.Sleep(500);
        }

        int _after = Driver.WindowHandles.Count;
        AppLogger.Info($"[DailySet]   已 Ctrl+点击（标签页数 {_before} -> {_after}）");

        if (_after <= _before)
        {
            AppLogger.Warning($"[DailySet]   Ctrl+点击后没有新标签页，当前页面: {Driver.Url}");
            return false;
        }

        // 切到新标签页，等它加载完
        string _latest = Driver.WindowHandles[^1];
        Driver.SwitchTo().Window(_latest);
        WaitForPageLoad();
        AppLogger.Info($"[DailySet]   任务页: {Driver.Url} / {Driver.Title}");
        return true;
    }

    /// <summary>
    /// 关闭任务标签页并切回 dashboard 主窗口
    /// </summary>
    private void CloseTaskTabs()
    {
        try
        {
            List<string> _handles = [.. Driver.WindowHandles];
            AppLogger.Debug($"[DailySet] 关闭前标签页数: {_handles.Count}，主窗口 {ShortHandle(MainWindowHandle)}");

            foreach (string _handle in _handles)
            {
                if (_handle == MainWindowHandle) continue;

                try
                {
                    Driver.SwitchTo().Window(_handle);
                    AppLogger.Debug($"[DailySet] 关闭标签页 {ShortHandle(_handle)}: {Driver.Url}");
                    Driver.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"关闭任务标签页失败: {ex.Message}");
                }
            }

            if (Driver.WindowHandles.Contains(MainWindowHandle))
            {
                Driver.SwitchTo().Window(MainWindowHandle);
                AppLogger.Debug($"[DailySet] 关闭后标签页数: {Driver.WindowHandles.Count}，已切回主窗口: {Driver.Url}");
                return;
            }

            // ! 主窗口被意外关掉时，退回剩余窗口并重新打开 dashboard
            AppLogger.Warning("[DailySet] 主窗口已不存在，切到剩余窗口并重新打开 dashboard");
            MainWindowHandle = Driver.WindowHandles[0];
            Driver.SwitchTo().Window(MainWindowHandle);
            NavigateToDashboard();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"整理标签页失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 把当前页面 HTML 存到 Logs/Debug，用于排查选择器失效
    /// </summary>
    private void DumpPage(string _tag)
    {
        try
        {
            string _dir = Path.Combine(AppContext.BaseDirectory, "Logs", "Debug");
            Directory.CreateDirectory(_dir);
            string _file = Path.Combine(_dir, $"{_tag}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(_file, Driver.PageSource);
            AppLogger.Info($"[DailySet] 页面已保存: {_file}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[DailySet] 保存页面失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 日志里截断长文本，避免刷屏
    /// </summary>
    private static string Preview(string _text, int _max_length)
    {
        string _flat = _text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return _flat.Length <= _max_length ? _flat : _flat[.._max_length] + "…";
    }

    /// <summary>
    /// 窗口句柄只保留后 6 位，日志里够区分
    /// </summary>
    private static string ShortHandle(string _handle)
    {
        return _handle.Length <= 6 ? _handle : "…" + _handle[^6..];
    }

    /// <summary>
    /// 执行 Punch Cards 任务
    /// </summary>
    public async Task<int> ExecutePunchCards()
    {
        if (!AppConfig.AutoPunchCards)
        {
            return 0;
        }

        int _completed = 0;
        try
        {
            AppLogger.Info($"正在导航到 Earn 页面: {AppConfig.EarnUrl}");
            Driver.Navigate().GoToUrl(AppConfig.EarnUrl);
            WaitForPageLoad();
            AppLogger.Info("Earn 页面已加载，正在扫描 Punch Cards...");

            ReadOnlyCollection<IWebElement> _cards = Driver.FindElements(By.CssSelector("[data-bi-name*='PunchCard']"));
            if (_cards.Count == 0)
            {
                AppLogger.Info("未找到 PunchCard 专属选择器，尝试通用选择器...");
                _cards = Driver.FindElements(By.CssSelector(".punch-card, .punchcard"));
            }
            AppLogger.Info($"发现 {_cards.Count} 个 Punch Card");

            foreach (IWebElement _card in _cards)
            {
                try
                {
                    if (_card.Text.Contains("已完成") || _card.Text.Contains("completed"))
                    {
                        continue;
                    }

                    IWebElement _link = _card.FindElement(By.TagName("a"));
                    string _name = _card.Text.Split('\n')[0];
                    _link.Click();
                    await Task.Delay(Random.Shared.Next(3000, 6000));

                    await CompletePunchCardSubTasks();

                    _completed++;
                    TotalPointsEarned += 50;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"执行 Punch Card 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("执行 Punch Cards 时发生异常", ex);
        }

        return _completed;
    }

    /// <summary>
    /// 执行日常任务（拼图、电影、日记等）
    /// </summary>
    public async Task<int> ExecuteDailyTasks()
    {
        if (!AppConfig.AutoDailyTasks)
        {
            return 0;
        }

        int _completed = 0;
        try
        {
            AppLogger.Info($"正在导航到 Earn 页面: {AppConfig.EarnUrl}");
            Driver.Navigate().GoToUrl(AppConfig.EarnUrl);
            WaitForPageLoad();
            AppLogger.Info("正在扫描日常任务...");

            ReadOnlyCollection<IWebElement> _links = Driver.FindElements(By.CssSelector("a[href*='offerId']"));
            AppLogger.Info($"发现 {_links.Count} 个任务链接");

            foreach (IWebElement _link in _links)
            {
                try
                {
                    string _href = _link.GetAttribute("href") ?? "";
                    if (string.IsNullOrEmpty(_href))
                    {
                        continue;
                    }

                    string _text = _link.Text;
                    if (_text.Contains("已完成") || _text.Contains("completed"))
                    {
                        continue;
                    }

                    AppLogger.Info($"正在执行日常任务: {_text}");
                    _link.Click();
                    await Task.Delay(Random.Shared.Next(5000, 10000));

                    await InteractWithTaskPage();

                    _completed++;
                    TotalPointsEarned += 5;

                    Driver.Navigate().GoToUrl(AppConfig.EarnUrl);
                    WaitForPageLoad();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"执行日常任务失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("执行日常任务时发生异常", ex);
        }

        return _completed;
    }

    private async Task CompletePunchCardSubTasks()
    {
        try
        {
            ReadOnlyCollection<IWebElement> _sub_links = Driver.FindElements(By.CssSelector("a[href*='PunchCard']"));
            foreach (IWebElement _sub in _sub_links)
            {
                try
                {
                    if (_sub.Text.Contains("已完成") || _sub.Text.Contains("completed"))
                    {
                        continue;
                    }

                    _sub.Click();
                    await Task.Delay(Random.Shared.Next(3000, 6000));
                    Driver.Navigate().Back();
                    await Task.Delay(Random.Shared.Next(1000, 2000));
                }
                catch
                {
                    // 忽略单个子任务失败
                }
            }
        }
        catch
        {
            // 忽略
        }
    }

    private async Task InteractWithTaskPage()
    {
        try
        {
            ReadOnlyCollection<IWebElement> _options = Driver.FindElements(By.CssSelector("button[data-bi-name*='option'], .option, .choice"));
            if (_options.Count > 0)
            {
                int _random_index = Random.Shared.Next(_options.Count);
                _options[_random_index].Click();
                await Task.Delay(Random.Shared.Next(2000, 4000));
            }
        }
        catch
        {
            // 忽略交互失败
        }
    }

    private void WaitForPageLoad()
    {
        try
        {
            // ! 简单等待页面加载，避免 WebDriverWait JavaScript 执行超时
            Task.Delay(2000).Wait();
        }
        catch
        {
            // 忽略
        }
    }
}

/// <summary>
/// Dashboard 状态信息
/// </summary>
public record DashboardStatus
{
    public int CurrentPoints { get; init; }
    public string Level { get; init; } = "";
    public DateTime Timestamp { get; init; }

    public override string ToString()
    {
        return $"当前积分: {CurrentPoints:N0}, 会员等级: {Level}, 更新时间: {Timestamp:HH:mm:ss}";
    }
}

/// <summary>
/// 今日进度信息
/// </summary>
public record TodayProgress
{
    public int ClaimableCount { get; set; }
    public (int Done, int Total) SearchProgress { get; set; }
    public (int Done, int Total) ActivityProgress { get; set; }
    public (int Done, int Total) CheckInProgress { get; set; }
    public int DailySetDone { get; set; }
    public int DailySetTotal { get; set; }
}
