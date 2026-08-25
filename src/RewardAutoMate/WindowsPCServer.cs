using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using RewardAutoMate.Configuration;
using RewardAutoMate.Logging;
using RewardAutoMate.Services;

namespace RewardAutoMate;

/// <summary>
/// 核心搜索与任务执行逻辑（集成反检测与行为模拟）
/// </summary>
public static class WindowsPCServer
{
    private static readonly Random Random = new();
    private static readonly List<string> ExecutedTasks = [];

    private static readonly string[] DefaultSearchWords =
    [
        // 科技领域
        "人工智能", "机器学习", "深度学习", "自然语言处理", "计算机视觉",
        "虚拟现实", "增强现实", "智能家居", "智能穿戴设备", "无人机",
        "新能源汽车", "自动驾驶技术", "芯片技术", "半导体产业", "量子通信",
        "区块链应用", "加密货币", "云计算服务", "大数据分析", "物联网平台",
        "5G网络建设", "6G技术研究", "操作系统创新", "编程语言趋势", "软件定义网络",
        "开源软件运动", "科技独角兽企业", "科技巨头并购", "科技伦理问题", "科技成果转化",

        // 娱乐领域
        "热门电影推荐", "奥斯卡金像奖", "戛纳电影节", "好莱坞大片", "国产电影佳作",
        "当红影视明星", "偶像团体新动态", "音乐颁奖典礼", "流行音乐榜单", "说唱音乐潮流",
        "舞蹈综艺节目", "喜剧小品热点", "动漫新番排行", "游戏电竞大赛", "网络小说爆款",
        "明星绯闻八卦", "影视翻拍争议", "音乐版权纠纷", "网红直播带货", "娱乐行业整顿",

        // 体育领域
        "足球世界杯", "欧洲杯足球赛", "篮球NBA赛事", "奥运会举办动态", "冬奥会精彩项目",
        "网球大满贯赛事", "田径世锦赛", "游泳锦标赛", "乒乓球世界冠军赛", "羽毛球公开赛",
        "高尔夫球职业赛", "赛车锦标赛", "拳击重量级对决", "体操全能冠军", "跳水明星风采",
        "体育明星转会", "体育赛事转播权", "全民健身热潮", "体育场馆建设", "体育产业发展",

        // 文化领域
        "古典诗词新解读", "文学名著翻拍", "书法艺术展览", "绘画流派探讨", "雕塑艺术新作",
        "博物馆新展亮点", "文化遗产保护", "非物质文化传承", "历史文化名城", "民俗文化节日",
        "茶文化品鉴", "酒文化溯源", "美食文化传播", "时尚文化潮流", "摄影艺术风格",
        "文化创意产业", "文化交流活动", "文化旅游融合", "文化品牌塑造", "文化市场监管",

        // 社会领域
        "教育双减政策", "高校招生改革", "职业教育发展", "就业市场形势", "创业扶持政策",
        "住房保障体系", "房地产市场调控", "医疗卫生改革", "养老服务模式", "社会保障制度",
        "交通拥堵治理", "新能源交通发展", "环境保护行动", "垃圾分类推广", "生态修复工程",
        "乡村振兴战略", "城市更新计划", "社区治理创新", "公益慈善事业", "社会公平正义",
        "网络安全问题", "信息隐私保护", "社交媒体管理", "舆情监测分析", "公共安全保障",

        // 健康领域
        "新冠疫情防控", "疫苗研发进展", "心理健康关注", "养生保健方法", "运动健身计划",
        "营养膳食搭配", "中医传统疗法", "康复医学发展", "医疗器械创新", "基因检测服务",
        "健康管理理念", "慢性病防治策略", "母婴健康呵护", "老年健康关怀", "美容护肤新趋势",

        // 经济领域
        "宏观经济形势", "货币政策调整", "财政政策动态", "国际贸易摩擦", "跨境电商发展",
        "金融科技应用", "资本市场波动", "企业数字化转型", "供应链安全保障", "消费市场趋势",
        "新兴产业投资", "传统产业升级", "中小企业扶持", "共享经济模式", "绿色经济发展",

        // 国际领域
        "中美关系走向", "俄乌冲突局势", "欧洲一体化进程", "中东地区局势", "亚太地区合作",
        "非洲发展机遇", "拉丁美洲经济", "国际贸易协定", "国际组织改革", "全球气候变化",

        // 政治领域
        "国内政治动态", "政府政策解读", "政治体制改革", "民主法治建设", "基层民主实践",
        "国际政治格局", "外交政策调整", "国际合作倡议", "地缘政治博弈", "国际冲突调解",

        // 持续扩展以达到 1000 个，此处为简化示意
        "新科技突破", "娱乐新热点", "体育新赛事", "文化新潮流", "社会新问题",
        "健康新观念", "经济新趋势", "国际新动态", "政治新举措", "教育新探索",
        "科技伦理规范", "娱乐行业自律", "体育精神弘扬", "文化传承创新", "社会治理现代化",
        "健康生活方式", "经济高质量发展", "国际合作共赢", "政治文明进步", "教育公平提升",
        "科技创新生态", "娱乐内容创新", "体育赛事商业化", "文化产业数字化", "社会民生改善",
        "健康医疗信息化", "经济结构优化", "国际秩序重塑", "政治体制活力", "教育质量提升",
        "科技人才培养", "娱乐市场规范", "体育设施升级", "文化交流互鉴", "社会和谐稳定",
        "健康养老服务", "经济开放合作", "国际和平与安全", "政治决策科学化", "教育创新驱动",
        "科技成果共享", "娱乐消费升级", "体育竞技水平", "文化品牌打造", "社会公平正义",
        "健康科普宣传", "经济风险管理", "国际发展合作", "政治参与度", "教育资源均衡",
        "科技基础设施", "娱乐行业融合", "体育文化传播", "文化遗产活化", "社会治理效能",
        "健康管理体系", "经济增长动力", "国际合作机制", "政治民主建设", "教育质量保障",
        "科技应用场景", "娱乐创新模式", "体育产业生态", "文化旅游体验", "社会民生保障",
        "健康服务质量", "经济结构调整", "国际合作平台", "政治制度优势", "教育人才培养",
        "科技安全保障", "娱乐市场监管", "体育赛事创新", "文化传承保护", "社会治理能力",
        "健康科技创新", "经济可持续发展", "国际合作领域", "政治文明建设", "教育改革深化",
        "科技企业竞争力", "娱乐文化内涵", "体育精神传承", "文化产业升级", "社会公平保障",
        "健康生活促进", "经济发展模式", "国际合作机遇", "政治参与渠道", "教育资源配置",
        "科技研发投入", "娱乐消费体验", "体育产业活力", "文化旅游融合度", "社会民生福祉",
        "健康服务创新", "经济增长潜力", "国际合作空间", "政治制度自信", "教育质量提升工程",
        "科技成果转化效率", "娱乐行业发展趋势", "体育赛事影响力", "文化遗产保护力度", "社会治理创新实践",
        "健康管理服务模式", "经济发展新动能", "国际合作新机制", "政治民主发展道路", "教育改革新举措",
        "科技企业创新生态", "娱乐文化创新活力", "体育精神弘扬途径", "文化产业数字化转型", "社会公平正义保障体系",
        "健康科技创新能力", "经济可持续发展路径", "国际合作领域拓展", "政治文明建设新高度", "教育改革深化方向",
        "科技人才队伍建设", "娱乐市场规范发展", "体育赛事商业化运作", "文化旅游融合发展战略", "社会民生改善行动计划",
        "健康服务质量提升", "经济结构优化升级", "国际合作平台搭建", "政治制度优势彰显", "教育质量保障体系完善",
        "科技应用场景拓展", "娱乐创新模式探索", "体育产业生态构建", "文化旅游体验升级", "社会治理效能提升行动",
        "健康管理体系完善", "经济增长动力转换", "国际合作机制创新", "政治民主建设新实践", "教育改革深化实践",
        "科技安全保障体系", "娱乐市场监管强化", "体育赛事创新发展", "文化传承保护工程", "社会治理能力现代化建设",
        "健康科技创新突破", "经济可持续发展战略", "国际合作领域扩大", "政治文明进步新台阶", "教育改革深化目标",
        "科技企业竞争力提升", "娱乐文化内涵挖掘", "体育精神传承创新", "文化产业升级转型", "社会公平保障机制",
        "健康生活促进计划", "经济发展模式创新", "国际合作机遇把握", "政治参与渠道拓宽", "教育资源配置优化",
        "科技研发投入加大", "娱乐消费体验升级", "体育产业活力激发", "文化旅游融合度提升", "社会民生福祉增进",
        "健康服务创新发展", "经济增长潜力释放", "国际合作空间拓展", "政治制度自信增强", "教育质量提升工程推进"
    ];

    private static readonly string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>
    /// 获取已执行任务列表
    /// </summary>
    public static List<string> GetExecutedTasks() => [.. ExecutedTasks];

    /// <summary>
    /// 清除已执行任务记录
    /// </summary>
    public static void ClearExecutedTasks() => ExecutedTasks.Clear();

    /// <summary>
    /// 随机获取一个搜索词
    /// </summary>
    private static string GetRandomSearchWord() => DefaultSearchWords[Random.Next(DefaultSearchWords.Length)];

    /// <summary>
    /// 生成指定长度的随机字符串
    /// </summary>
    private static string GenerateRandomString(int _length)
    {
        string _result = "";
        for (int i = 0; i < _length; i++)
        {
            _result += Characters[Random.Next(Characters.Length)];
        }
        return _result;
    }

    /// <summary>
    /// 带重试的搜索任务封装
    /// </summary>
    public static async Task<uint> AutoSearchWithRetry(bool _user_agent = false, IProgress<(int done, int total)>? _progress = null)
    {
        uint _total_count = 0;
        int _max_retries = AppConfig.MaxRetryCount;

        for (int _retry = 0; _retry < _max_retries; _retry++)
        {
            if (_retry > 0)
            {
                int _wait = Random.Next(10, 30);
                AppLogger.Info($"搜索重试准备中... 第 {_retry + 1}/{_max_retries} 次尝试，等待 {_wait} 秒...");
                await Task.Delay(TimeSpan.FromSeconds(_wait));
            }

            uint _count = await AutoSearch(_user_agent, _progress);
            _total_count += _count;

            // ? 如果已完成全部搜索，直接返回
            int _target = _user_agent ? AppConfig.MaxMobileSearches : AppConfig.MaxPcSearches;
            if (_total_count >= _target)
            {
                return _total_count;
            }

            // ? 如果本次执行了至少 3 次搜索，认为部分成功，继续尝试完成剩余
            if (_count >= 3)
            {
                AppLogger.Info($"部分成功: 本次执行 {_count} 次，累计 {_total_count}/{_target}，继续尝试...");
                continue;
            }

            // ? 如果本次执行少于 3 次且不是最后一次重试，继续重试
            if (_count < 3 && _retry < _max_retries - 1)
            {
                AppLogger.Warning($"本次仅执行 {_count} 次，将重试...");
                continue;
            }

            // ? 最后一次重试仍然失败，返回已执行的总数
            break;
        }

        return _total_count;
    }

    /// <summary>
    /// 执行搜索任务（PC 端或移动端），集成反检测措施
    /// </summary>
    public static async Task<uint> AutoSearch(bool _user_agent = false, IProgress<(int done, int total)>? _progress = null)
    {
        System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        EdgeDriver? _driver = null;
        uint _count = 0;
        CancellationTokenSource _cts = new CancellationTokenSource();

        try
        {
            string _platform = _user_agent ? "移动端" : "PC端";
            AppLogger.Info($"==== 启动 {_platform} 搜索任务 ====");
            AppLogger.Info($"目标搜索次数: {(_user_agent ? AppConfig.MaxMobileSearches : AppConfig.MaxPcSearches)}，批次大小: {AppConfig.BatchSize}，暂停间隔: {AppConfig.PauseTimeMinute}分钟");

            // ! 创建 EdgeDriver（已集成反检测）
            _driver = EdgeDriverFactory.CreateDriver(_user_agent);
            int _max_searches = _user_agent ? AppConfig.MaxMobileSearches : AppConfig.MaxPcSearches;

            // ? 随机化批次大小（避免固定模式）
            int _batch_size = HumanBehaviorSimulator.GetRandomizedBatchSize(AppConfig.BatchSize);
            AppLogger.Info($"本次随机批次大小: {_batch_size}");

            // 报告初始进度
            _progress?.Report((0, _max_searches));

            for (_count = 0; _count < _max_searches; _count++)
            {
                // - 批次暂停（随机化间隔）
                if (_count != 0 && _count % _batch_size == 0)
                {
                    TimeSpan _pause = HumanBehaviorSimulator.GetRandomizedInterval(TimeSpan.FromMinutes(AppConfig.PauseTimeMinute));
                    int _remaining = _max_searches - (int)_count;
                    AppLogger.Warning($"批次完成 ({_count}/{_max_searches})，暂停 {_pause.TotalMinutes:F1} 分钟，剩余 {_remaining} 次搜索...");
                    await Task.Delay(_pause, _cts.Token);

                    // ? 随机更换批次大小
                    _batch_size = HumanBehaviorSimulator.GetRandomizedBatchSize(AppConfig.BatchSize);
                    AppLogger.Info($"新批次大小: {_batch_size}");
                }

                // 报告实时进度
                _progress?.Report(((int)_count + 1, _max_searches));

                string _search_word = GetRandomSearchWord();
                string _random_string = GenerateRandomString(4);
                string _rand_cvid = GenerateRandomString(32);
                string _search_url = _count <= _max_searches / 2
                    ? $"https://www.bing.com/search?q={Uri.EscapeDataString(_search_word)}&form={_random_string}&cvid={_rand_cvid}"
                    : $"https://cn.bing.com/search?q={Uri.EscapeDataString(_search_word)}&form={_random_string}&cvid={_rand_cvid}";

                AppLogger.Debug($"【{DateTime.Today:yyyy-MM-dd}】【{_count + 1}/{_max_searches}】正在搜索：{_search_word}...");

                // ! 导航到搜索页面（带超时保护）
                AppLogger.Debug($"  -> 导航到搜索页面...");
                try
                {
                    _driver.Navigate().GoToUrl(_search_url);
                }
                catch (WebDriverException navEx) when (navEx.Message.Contains("timed out") || navEx.Message.Contains("timeout"))
                {
                    AppLogger.Warning($"  -> 导航超时，跳过本次搜索: {navEx.Message}");
                    // 尝试刷新页面恢复状态
                    try { _driver.Navigate().Refresh(); } catch { /* 忽略 */ }
                    await Task.Delay(Random.Next(3000, 8000), _cts.Token);
                    continue;
                }

                int _nav_delay = Random.Next(AppConfig.SearchDelayMinMs, AppConfig.SearchDelayMaxMs);
                AppLogger.Debug($"  -> 页面加载后等待 {_nav_delay / 1000.0:F1} 秒...");
                await Task.Delay(_nav_delay, _cts.Token);

                // ! 自然滚动（替代原来的固定脚本）
                AppLogger.Debug($"  -> 开始自然滚动...");
                await HumanBehaviorSimulator.NaturalScrollPage(_driver, _cts.Token);

                // ? 模拟阅读行为
                int _read_time = Random.Next(3, 9);
                AppLogger.Debug($"  -> 模拟阅读 {_read_time} 秒...");
                await HumanBehaviorSimulator.SimulateReading(_driver, _read_time, _read_time + 1, _cts.Token);

                // ? 偶尔访问非搜索页面（增加行为多样性，约 5% 概率）
                if (Random.Next(20) == 0)
                {
                    AppLogger.Info($"  -> [行为多样化] 随机访问非搜索页面...");
                    await HumanBehaviorSimulator.VisitRandomPage(_driver, _cts.Token);
                    AppLogger.Debug($"  -> 返回搜索页继续...");
                    _driver.Navigate().Back();
                    await Task.Delay(Random.Next(1000, 3000), _cts.Token);
                }

                int _random_delay = Random.Next(AppConfig.PageStayMinMs, AppConfig.PageStayMaxMs);
                AppLogger.Debug($"  -> 页面停留 {_random_delay / 1000.0:F1} 秒...");
                await Task.Delay(_random_delay, _cts.Token);

                ExecutedTasks.Add($"搜索: {_search_word}");
            }

            AppLogger.Info($"【{_platform}搜索完成】共 {_count} 次，耗时 {_stopwatch.Elapsed:hh\\:mm\\:ss}，平均每次 {_stopwatch.Elapsed.TotalSeconds / Math.Max(1, _count):F1} 秒");
            return _count;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warning("搜索任务被取消");
            return _count;
        }
        catch (Exception ex)
        {
            // ! 记录详细异常信息（包含 inner exception）
            string _inner = ex.InnerException != null ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
            AppLogger.Error($"搜索执行异常: {ex.GetType().Name}: {ex.Message}{_inner}", ex);

            // ! 发送异常邮件通知（不阻塞主流程）
            try
            {
                await EmailNotificationService.SendExceptionNotificationAsync(
                    ex.GetType().Name,
                    ex.Message,
                    ex.ToString(),
                    "搜索任务执行中",
                    GetExecutedTasks());
            }
            catch (Exception notifyEx)
            {
                AppLogger.Warning($"异常通知发送失败: {notifyEx.Message}");
            }

            return _count;
        }
        finally
        {
            // ! 安全释放 CancellationTokenSource
            try { _cts.Cancel(); } catch { /* 忽略 */ }
            try { _cts.Dispose(); } catch { /* 忽略 */ }
            EdgeDriverFactory.QuitActiveDriver();
        }
    }

    /// <summary>
    /// 执行完整的 Rewards 任务流程（搜索 + Dashboard 任务）
    /// </summary>
    public static async Task<ExecuteResult> ExecuteFullRewardsFlow(
        IProgress<(int done, int total)>? _pc_progress = null,
        IProgress<(int done, int total)>? _mobile_progress = null)
    {
        ExecuteResult _result = new ExecuteResult();
        ClearExecutedTasks();

        try
        {
            // ! Step 1: PC 端搜索（带重试）
            AppLogger.Info("==== Phase 1/4: PC 端搜索 ====");
            uint _pc_count = await AutoSearchWithRetry(false, _pc_progress);
            _result.PcSearches = (int)_pc_count;
            _result.TotalSearches += (int)_pc_count;

            // ? 如果 PC 搜索完全失败，跳过移动端搜索
            if (_pc_count == 0)
            {
                AppLogger.Warning("PC 端搜索完全失败，跳过移动端搜索和后续步骤");
                _result.Success = false;
                _result.ErrorMessage = "PC 端搜索完全失败";
                return _result;
            }

            // ? 搜索间的随机延迟
            int _switch_delay = Random.Next(30000, 120000);
            AppLogger.Info($"PC 端搜索完成，切换移动端前等待 {_switch_delay / 1000} 秒...");
            await Task.Delay(_switch_delay);

            // ! Step 2: 移动端搜索（带重试）
            AppLogger.Info("==== Phase 2/4: 移动端搜索 ====");
            uint _mobile_count = await AutoSearchWithRetry(true, _mobile_progress);
            _result.MobileSearches = (int)_mobile_count;
            _result.TotalSearches += (int)_mobile_count;

            // ! Step 3: Dashboard 任务（如果启用）
            if (AppConfig.EnableDashboardTasks)
            {
                AppLogger.Info("==== Phase 3/4: Dashboard 任务 ====");
                AppLogger.Info($"配置: 福利领取={AppConfig.AutoClaimBenefits}, DailySet={AppConfig.AutoDailySet}, PunchCards={AppConfig.AutoPunchCards}, 日常任务={AppConfig.AutoDailyTasks}");

                EdgeDriver _driver = EdgeDriverFactory.CreateDriver(false);
                try
                {
                    RewardsDashboardService _dashboard = new RewardsDashboardService(_driver);

                    // - 读取状态
                    AppLogger.Info("正在读取 Dashboard 状态...");
                    DashboardStatus _status = _dashboard.GetDashboardStatus();
                    _result.CurrentPoints = _status.CurrentPoints;
                    AppLogger.Info($"当前账户状态: {_status}");

                    // - 领取福利
                    AppLogger.Info("[1/4] 开始领取福利...");
                    int _benefits = _dashboard.ClaimBenefits();
                    _result.BenefitsClaimed = _benefits;
                    AppLogger.Info($"[1/4] 福利领取完成: {_benefits} 个");

                    // - Daily Set
                    AppLogger.Info("[2/4] 开始执行 Daily Set...");
                    int _daily_set = await _dashboard.ExecuteDailySet();
                    _result.DailySetCompleted = _daily_set;
                    AppLogger.Info($"[2/4] Daily Set 完成: {_daily_set} 项");

                    // - Punch Cards
                    AppLogger.Info("[3/4] 开始执行 Punch Cards...");
                    int _punch_cards = await _dashboard.ExecutePunchCards();
                    _result.PunchCardsCompleted = _punch_cards;
                    AppLogger.Info($"[3/4] Punch Cards 完成: {_punch_cards} 张");

                    // - 日常任务
                    AppLogger.Info("[4/4] 开始执行日常任务...");
                    int _daily_tasks = await _dashboard.ExecuteDailyTasks();
                    _result.DailyTasksCompleted = _daily_tasks;
                    AppLogger.Info($"[4/4] 日常任务完成: {_daily_tasks} 个");

                    _result.TotalTasks = _daily_set + _punch_cards + _daily_tasks;
                    _result.EstimatedPoints = _dashboard.GetTotalPoints();
                    AppLogger.Info($"Dashboard 任务总计: {_result.TotalTasks} 项，估计积分: {_result.EstimatedPoints}");
                }
                finally
                {
                    EdgeDriverFactory.QuitActiveDriver();
                }
            }
            else
            {
                AppLogger.Info("Dashboard 任务已跳过（EnableDashboardTasks=false）");
            }

            // ! Step 4: 收尾与报告
            AppLogger.Info("==== Phase 4/4: 执行报告 ====");

            _result.Success = true;
            _result.EndTime = DateTime.Now;
            TimeSpan _total_time = _result.EndTime - _result.StartTime;

            AppLogger.Info($"总耗时: {_total_time:hh\\:mm\\:ss}");
            AppLogger.Info($"搜索统计: PC {_result.PcSearches} + Mobile {_result.MobileSearches} = {_result.TotalSearches}");
            AppLogger.Info($"任务统计: 福利 {_result.BenefitsClaimed}, DailySet {_result.DailySetCompleted}, PunchCards {_result.PunchCardsCompleted}, 日常 {_result.DailyTasksCompleted}");
            AppLogger.Info($"估计获得积分: {_result.EstimatedPoints}");
            AppLogger.Info($"已执行任务数: {GetExecutedTasks().Count}");

            // - 记录执行历史（供周报汇总查看与邮件报告）
            ExecutionHistoryService.Add(_result);
            AppLogger.Info($"执行历史已保存，累计搜索 {_result.TotalSearches} 次，估计积分 +{_result.EstimatedPoints}");

            // - 周报由独立定时器在每周一 09:00 触发，此处不发送邮件

            AppLogger.Info("==== 本次执行全部完成 ====");
            return _result;
        }
        catch (Exception ex)
        {
            _result.Success = false;
            _result.ErrorMessage = ex.Message;
            _result.EndTime = DateTime.Now;

            AppLogger.Error("完整 Rewards 流程执行异常", ex);

            await EmailNotificationService.SendExceptionNotificationAsync(
                ex.GetType().Name,
                ex.Message,
                ex.ToString(),
                "完整流程执行中",
                GetExecutedTasks());

            return _result;
        }
    }
}

/// <summary>
/// 执行结果汇总
/// </summary>
public record ExecuteResult
{
    public bool Success { get; set; }
    public int TotalSearches { get; set; }
    public int PcSearches { get; set; }
    public int MobileSearches { get; set; }
    public int BenefitsClaimed { get; set; }
    public int DailySetCompleted { get; set; }
    public int PunchCardsCompleted { get; set; }
    public int DailyTasksCompleted { get; set; }
    public int TotalTasks { get; set; }
    public int EstimatedPoints { get; set; }
    public int CurrentPoints { get; set; }
    public string ErrorMessage { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime EndTime { get; set; }

    public override string ToString()
    {
        return $"""
            执行结果: {(Success ? "成功" : "失败")}
            搜索次数: PC {PcSearches} + Mobile {MobileSearches} = {TotalSearches}
            福利领取: {BenefitsClaimed}
            Daily Set: {DailySetCompleted}
            Punch Cards: {PunchCardsCompleted}
            日常任务: {DailyTasksCompleted}
            估计积分: {EstimatedPoints}
            {(string.IsNullOrEmpty(ErrorMessage) ? "" : $"错误信息: {ErrorMessage}")}
            """;
    }
}
