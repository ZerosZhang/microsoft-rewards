namespace RewardAutoMate.Configuration;

/// <summary>
/// 应用全局配置，所有可调整参数集中于此
/// </summary>
public static class AppConfig
{
    #region 搜索任务配置

    /// <summary>
    /// PC 端每日最大搜索次数
    /// </summary>
    public static int MaxPcSearches { get; set; } = 50;

    /// <summary>
    /// 移动端每日最大搜索次数
    /// </summary>
    public static int MaxMobileSearches { get; set; } = 50;

    /// <summary>
    /// 每批次搜索后暂停时长（分钟）
    /// </summary>
    public static int PauseTimeMinute { get; set; } = 15;

    /// <summary>
    /// 每批次搜索数量
    /// </summary>
    public static int BatchSize { get; set; } = 5;

    /// <summary>
    /// 搜索间隔最小延迟（毫秒）
    /// </summary>
    public static int SearchDelayMinMs { get; set; } = 1000;

    /// <summary>
    /// 搜索间隔最大延迟（毫秒）
    /// </summary>
    public static int SearchDelayMaxMs { get; set; } = 30000;

    /// <summary>
    /// 页面停留最小延迟（毫秒）
    /// </summary>
    public static int PageStayMinMs { get; set; } = 10000;

    /// <summary>
    /// 页面停留最大延迟（毫秒）
    /// </summary>
    public static int PageStayMaxMs { get; set; } = 30000;

    /// <summary>
    /// 执行间隔（小时）
    /// </summary>
    public static double ExecuteIntervalHours { get; set; } = 12;

    #endregion

    #region Dashboard 任务配置

    /// <summary>
    /// Rewards Dashboard URL
    /// </summary>
    public static string DashboardUrl { get; set; } = "https://rewards.bing.com/dashboard";

    /// <summary>
    /// Rewards 赚取页面 URL
    /// </summary>
    public static string EarnUrl { get; set; } = "https://rewards.bing.com/earn";

    /// <summary>
    /// 是否启用 Dashboard 任务自动化
    /// </summary>
    public static bool EnableDashboardTasks { get; set; } = true;

    /// <summary>
    /// 是否自动领取福利区域积分
    /// </summary>
    public static bool AutoClaimBenefits { get; set; } = true;

    /// <summary>
    /// 是否自动执行 Daily Set
    /// </summary>
    public static bool AutoDailySet { get; set; } = true;

    /// <summary>
    /// 是否自动执行 Punch Cards
    /// </summary>
    public static bool AutoPunchCards { get; set; } = true;

    /// <summary>
    /// 是否自动执行日常任务
    /// </summary>
    public static bool AutoDailyTasks { get; set; } = true;

    /// <summary>
    /// 任务执行前等待页面加载最大时间（秒）
    /// </summary>
    public static int PageLoadTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 元素查找超时（秒）
    /// </summary>
    public static int ElementTimeoutSeconds { get; set; } = 10;

    #endregion

    #region 浏览器配置

    /// <summary>
    /// Edge 用户数据目录路径
    /// </summary>
    public static string EdgeUserDataDir { get; set; } = "";

    /// <summary>
    /// 移动端 User-Agent
    /// </summary>
    public static string MobileUserAgent { get; set; } = "Mozilla/5.0 (iPhone; CPU iPhone OS 13_2_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/13.0.3 Mobile/15E148 Safari/604.1";

    /// <summary>
    /// 是否使用无头模式（后台运行，不弹浏览器窗口；服务器部署建议开启）
    /// </summary>
    public static bool Headless { get; set; } = true;

    #endregion

    #region 邮件通知配置

    /// <summary>
    /// 是否启用异常邮件通知
    /// </summary>
    public static bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// SMTP 服务器地址（示例为 QQ 邮箱）
    /// </summary>
    public static string SmtpServer { get; set; } = "smtp.qq.com";

    /// <summary>
    /// SMTP 端口（QQ/163 邮箱 STARTTLS 端口为 587；.NET SmtpClient 对 465 隐式 SSL 兼容性差）
    /// </summary>
    public static int SmtpPort { get; set; } = 587;

    /// <summary>
    /// 发件人邮箱（QQ 邮箱）
    /// </summary>
    public static string SmtpFrom { get; set; } = "zeros_zhang@qq.com";

    /// <summary>
    /// 收件人邮箱列表（逗号分隔；实际值由 External/email_config.json 覆盖）
    /// </summary>
    public static string SmtpTo { get; set; } = "zeros_zhang@qq.com";

    /// <summary>
    /// SMTP 授权码/密码（QQ/163 需使用邮箱设置的"授权码"而非登录密码）
    /// </summary>
    public static string SmtpPassword { get; set; } = "";

    /// <summary>
    /// 是否启用 SSL
    /// </summary>
    public static bool SmtpEnableSsl { get; set; } = true;

    /// <summary>
    /// 相同异常通知冷却时间（分钟）
    /// </summary>
    public static int NotifyCooldownMinutes { get; set; } = 30;

    /// <summary>
    /// 是否发送每周执行汇总邮件（每周一 09:00 定时发送，含近 7 天概况）
    /// </summary>
    public static bool SendWeeklyReport { get; set; } = true;

    #endregion

    #region 反检测配置

    /// <summary>
    /// 每次操作前额外随机延迟范围（毫秒）
    /// </summary>
    public static int ExtraRandomDelayMaxMs { get; set; } = 2000;

    /// <summary>
    /// 是否模拟鼠标轨迹
    /// </summary>
    public static bool SimulateMouseMovement { get; set; } = true;

    /// <summary>
    /// 失败重试次数
    /// </summary>
    public static int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 是否自动同步 WebDriver 版本（与本机 Edge 匹配）
    /// </summary>
    public static bool AutoUpdateWebDriver { get; set; } = true;

    /// <summary>
    /// 是否输出 msedgedriver 详细日志到 Logs\msedgedriver.log（排查浏览器启动失败用）
    /// </summary>
    public static bool EnableDriverVerboseLog { get; set; } = true;

    #endregion

    /// <summary>
    /// 获取 Edge 用户数据目录完整路径
    /// </summary>
    public static string GetEdgeUserDataFullPath()
    {
        if (string.IsNullOrEmpty(EdgeUserDataDir))
        {
            return System.IO.Path.Combine(AppContext.BaseDirectory, "EdgeUserData");
        }
        return EdgeUserDataDir;
    }
}
