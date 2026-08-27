using System.Collections.ObjectModel;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
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

            // ! 统计 Daily Set 完成数 - 精确匹配"每日活动"区域
            // 每日活动卡片特征：同时包含 "+10" 和 "已完成"
            // 福利区域的"已完成"后跟的是具体积分数字（如 900, 180），不是 "+10"
            try
            {
                int _daily_completed = 0;

                // 方法1：遍历所有 p 标签，查找同时包含 "已完成" 和 "+10" 的
                ReadOnlyCollection<IWebElement> _all_ps = Driver.FindElements(By.CssSelector("p"));
                foreach (IWebElement _p in _all_ps)
                {
                    string _text = _p.Text.Trim();
                    if (_text.Contains("已完成") && _text.Contains("+10"))
                    {
                        _daily_completed++;
                    }
                }

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
                        }
                    }
                }

                _progress.DailySetDone = Math.Min(_daily_completed, 3);
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
    /// </summary>
    public async Task<int> ExecuteDailySet()
    {
        if (!AppConfig.AutoDailySet)
        {
            return 0;
        }

        int _completed = 0;
        IList<IWebElement> _cards = [];
        try
        {
            NavigateToDashboard();

            AppLogger.Info("正在扫描 Daily Set 任务卡片...");
            _cards = Driver.FindElements(By.CssSelector("[data-bi-name*='DailySet']"));
            if (_cards.Count == 0)
            {
                AppLogger.Info("未找到 DailySet 专属选择器，尝试通用选择器...");
                _cards = Driver.FindElements(By.CssSelector(".reward-card, .offer-card, .mee-card"));
            }
            if (_cards.Count == 0)
            {
                // ? 尝试更通用的卡片选择器
                _cards = Driver.FindElements(By.CssSelector("[class*='Card'], [class*='card']"));
            }
            AppLogger.Info($"发现 {_cards.Count} 个任务卡片");

            int _card_index = 0;
            foreach (IWebElement _card in _cards)
            {
                _card_index++;
                try
                {
                    string _card_text = _card.Text.ToLower();
                    string _card_preview = _card.Text.Split('\n')[0];
                    AppLogger.Debug($"  检查卡片 [{_card_index}/{_cards.Count}]: {_card_preview[..Math.Min(30, _card_preview.Length)]}...");

                    // ? 跳过已完成的任务
                    if (_card_text.Contains("已完成") || _card_text.Contains("completed") || _card_text.Contains("done"))
                    {
                        AppLogger.Debug($"    -> 已跳过（已完成）");
                        continue;
                    }

                    // 查找卡片内的链接或按钮
                    IWebElement? _action = null;
                    try
                    {
                        _action = _card.FindElement(By.TagName("a"));
                    }
                    catch
                    {
                        try
                        {
                            _action = _card.FindElement(By.TagName("button"));
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    string _task_name = _card.Text.Split('\n')[0];
                    AppLogger.Info($"正在执行 Daily Set 任务: {_task_name}");

                    _action.Click();
                    await Task.Delay(Random.Shared.Next(2000, 5000));

                    // ? 如果是搜索任务，通常会自动跳转，等待一下返回
                    if (_card_text.Contains("搜索") || _card_text.Contains("search"))
                    {
                        await Task.Delay(Random.Shared.Next(3000, 8000));
                        Driver.Navigate().Back();
                        await Task.Delay(Random.Shared.Next(1000, 3000));
                    }
                    else
                    {
                        await Task.Delay(Random.Shared.Next(5000, 10000));
                        Driver.Navigate().Back();
                    }

                    _completed++;
                    TotalPointsEarned += 10;
                    AppLogger.Info($"    -> Daily Set 任务完成 [{_completed}]");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"  执行 Daily Set 单项任务失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("执行 Daily Set 时发生异常", ex);
        }

        AppLogger.Info($"Daily Set 总计完成: {_completed}/{_cards.Count} 项");
        return _completed;
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
