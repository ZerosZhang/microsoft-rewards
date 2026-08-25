using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using RewardAutoMate.Configuration;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// Edge WebDriver 工厂，统一创建和管理浏览器实例（集成反检测）
/// </summary>
public static class EdgeDriverFactory
{
    private static EdgeDriver? ActiveDriver = null;
    private static readonly object Lock = new object();

    /// <summary>
    /// 创建新的 EdgeDriver 实例（带反检测配置）
    /// </summary>
    public static EdgeDriver CreateDriver(bool _is_mobile = false, bool _reuse_data_dir = true)
    {
        lock (Lock)
        {
            // ! 如果已有活动实例，先关闭
            if (ActiveDriver is not null)
            {
                try
                {
                    ActiveDriver.Quit();
                }
                catch
                {
                    // 忽略关闭异常
                }
                ActiveDriver = null;
            }

            // ! 确保驱动版本与本机 Edge 匹配（自动同步）
            string _driver_path = WebDriverUpdater.EnsureDriverUpToDate();
            string _driver_dir = Path.GetDirectoryName(_driver_path) ?? AppContext.BaseDirectory;

            EdgeDriverService _service = EdgeDriverService.CreateDefaultService(_driver_dir);

            // ? 隐藏驱动控制台窗口
            _service.HideCommandPromptWindow = true;

            EdgeOptions _options = new EdgeOptions();

            // - 使用项目独立的用户数据目录
            if (_reuse_data_dir)
            {
                string _user_data_dir = AppConfig.GetEdgeUserDataFullPath();
                if (!Directory.Exists(_user_data_dir))
                {
                    Directory.CreateDirectory(_user_data_dir);
                }
                _options.AddArgument($"--user-data-dir={_user_data_dir}");
            }

            // ! 反检测核心参数
            _options.AddArgument("--no-sandbox");
            _options.AddArgument("--disable-dev-shm-usage");
            _options.AddArgument("--disable-blink-features=AutomationControlled");
            _options.AddExcludedArgument("enable-automation");
            _options.AddAdditionalOption("useAutomationExtension", false);

            // - 禁用信息栏
            _options.AddArgument("--disable-infobars");
            _options.AddArgument("--disable-extensions");
            _options.AddArgument("--disable-popup-blocking");

            // - 禁用同源策略限制
            _options.AddArgument("--disable-web-security");
            _options.AddArgument("--disable-features=IsolateOrigins,site-per-process");

            // ? 随机窗口尺寸（桌面端）
            string _ua;
            if (!_is_mobile)
            {
                (int _width, int _height) = AntiDetectionService.GetRandomWindowSize();
                _options.AddArgument($"--window-size={_width},{_height}");
                _options.AddArgument("--start-maximized");
                AppLogger.Info($"桌面端窗口尺寸: {_width}x{_height}");
            }
            else
            {
                _options.AddArgument("--window-size=390,844");
                _options.AddArgument("--force-device-scale-factor=3");
                AppLogger.Info("移动端窗口尺寸: 390x844 (iPhone 14 Pro 模拟)");
            }

            // ? 随机 User-Agent
            if (_is_mobile)
            {
                _ua = AntiDetectionService.GetRandomMobileUserAgent();
                _options.AddArgument($"user-agent={_ua}");
            }
            else
            {
                _ua = AntiDetectionService.GetRandomDesktopUserAgent();
                _options.AddArgument($"user-agent={_ua}");
            }
            AppLogger.Debug($"本次使用 User-Agent: {_ua[..Math.Min(80, _ua.Length)]}...");

            // - 语言设置
            _options.AddArgument("--lang=zh-CN,zh,en-US,en");

            if (AppConfig.Headless)
            {
                _options.AddArgument("--headless=new");
            }

            try
            {
                ActiveDriver = new EdgeDriver(_service, _options);
                // ! 设置页面加载超时：15秒（避免卡住60秒才抛异常）
                ActiveDriver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(15);
                ActiveDriver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(AppConfig.ElementTimeoutSeconds);
                // ! 脚本执行超时：10秒
                ActiveDriver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(10);

                // ! 注入反检测脚本
                AntiDetectionService.ApplyStealthScripts(ActiveDriver);
                AntiDetectionService.RandomizeWebGLFingerprint(ActiveDriver);
                AntiDetectionService.RandomizeCanvasFingerprint(ActiveDriver);

                AppLogger.Info($"EdgeDriver 创建成功 [Mobile={_is_mobile}]，反检测已启用，页面加载超时={AppConfig.PageLoadTimeoutSeconds}秒");
                return ActiveDriver;
            }
            catch (Exception ex)
            {
                AppLogger.Error("EdgeDriver 创建失败", ex);
                throw;
            }
        }
    }

    /// <summary>
    /// 获取当前活动的 Driver，如不存在则返回 null
    /// </summary>
    public static EdgeDriver? GetActiveDriver()
    {
        lock (Lock)
        {
            if (ActiveDriver is null)
            {
                return null;
            }

            try
            {
                // ! 简单验证 driver 是否仍有效
                _ = ActiveDriver.Title;
                return ActiveDriver;
            }
            catch
            {
                ActiveDriver = null;
                return null;
            }
        }
    }

    /// <summary>
    /// 关闭当前活动的 Driver
    /// </summary>
    public static void QuitActiveDriver()
    {
        lock (Lock)
        {
            if (ActiveDriver is not null)
            {
                try
                {
                    ActiveDriver.Quit();
                    AppLogger.Info("EdgeDriver 已关闭");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"关闭 EdgeDriver 时发生异常: {ex.Message}");
                }
                finally
                {
                    ActiveDriver = null;
                }
            }
        }
    }
}
