using System.Diagnostics;
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
    private static bool StartupCleaned = false;

    /// <summary>
    /// 创建新的 EdgeDriver 实例（带反检测配置）
    /// ! 崩溃重试走三级降级：原参数 -> 关 GPU/去最大化 -> 换备用 profile
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

            EnsureStartupCleanup();

            string? _user_data_dir = _reuse_data_dir ? ResolveUserDataDir() : null;

            const int _max_attempts = 3;
            for (int _attempt = 1; _attempt <= _max_attempts; _attempt++)
            {
                try
                {
                    return CreateDriverInternal(_is_mobile, _user_data_dir, _attempt);
                }
                catch (Exception ex)
                {
                    bool _crash = IsEdgeCrash(ex);
                    AppLogger.Error($"EdgeDriver 创建失败（第 {_attempt}/{_max_attempts} 次）", ex);
                    if (!_crash || _attempt == _max_attempts)
                    {
                        throw;
                    }

                    AppLogger.Warning($"Edge 启动崩溃，清理残留进程与锁文件后重试: {ex.Message}");
                    ProcessCleanupService.Cleanup(_user_data_dir, $"启动重试 {_attempt}");

                    // ! 第 3 次改用备用 profile，跳过原 profile 可能存在的损坏状态
                    if (_attempt == _max_attempts - 1 && _user_data_dir is not null)
                    {
                        string _fallback = GetFallbackUserDataDir();
                        AppLogger.Warning($"原用户目录仍不可用，改用备用用户目录: {_fallback}（该目录可能没有登录态，需要重新登录一次）");
                        _user_data_dir = _fallback;
                    }
                }
            }

            throw new WebDriverException("EdgeDriver 创建失败：已达到最大重试次数");
        }
    }

    /// <summary>
    /// 首次创建驱动前清理上一次运行（含异常退出）留下的浏览器/驱动进程与锁文件
    /// </summary>
    private static void EnsureStartupCleanup()
    {
        if (StartupCleaned)
        {
            return;
        }
        StartupCleaned = true;
        AppLogger.Info("启动检查：清理上一次运行可能残留的浏览器与驱动进程");
        ProcessCleanupService.Cleanup(AppConfig.GetEdgeUserDataFullPath(), "启动检查");
    }

    /// <summary>
    /// 解析用户数据目录并检查可写；不可写时退到备用目录（写不进去 Edge 会直接崩）
    /// </summary>
    private static string ResolveUserDataDir()
    {
        string _dir = AppConfig.GetEdgeUserDataFullPath();
        try
        {
            Directory.CreateDirectory(_dir);
            string _probe = Path.Combine(_dir, $".writetest_{Environment.ProcessId}");
            File.WriteAllText(_probe, "ok");
            File.Delete(_probe);
            return _dir;
        }
        catch (Exception ex)
        {
            string _fallback = GetFallbackUserDataDir();
            AppLogger.Error($"用户数据目录不可写，改用备用目录 {_fallback}: {_dir}", ex);
            return _fallback;
        }
    }

    private static string GetFallbackUserDataDir()
    {
        string _dir = Path.Combine(AppContext.BaseDirectory, "EdgeUserData.fallback");
        Directory.CreateDirectory(_dir);
        return _dir;
    }

    /// <summary>
    /// 创建 EdgeDriver 实例（含版本同步、反检测参数、脚本注入）
    /// </summary>
    private static EdgeDriver CreateDriverInternal(bool _is_mobile, string? _user_data_dir, int _attempt)
    {
        // ! 确保驱动版本与本机 Edge 匹配（自动同步）
        string _driver_path = WebDriverUpdater.EnsureDriverUpToDate();
        string _driver_dir = Path.GetDirectoryName(_driver_path) ?? AppContext.BaseDirectory;

        EdgeDriverService _service = EdgeDriverService.CreateDefaultService(_driver_dir);

        // ? 隐藏驱动控制台窗口
        _service.HideCommandPromptWindow = true;

        // ! 只开 --verbose；不要设 LogPath —— 实测 Selenium 4.44 + msedgedriver 155 下，
        //     设置 LogPath（任何路径，含 ASCII）都会让驱动进程还没初始化就退出：
        //     "Driver service process exited unexpectedly before initialization completed"
        if (AppConfig.EnableDriverVerboseLog)
        {
            _service.EnableVerboseLogging = true;
        }

        EdgeOptions _options = new EdgeOptions();

        // - 使用项目独立的用户数据目录
        if (_user_data_dir is not null)
        {
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

        // ? 随机窗口尺寸（桌面端）
        string _ua;
        if (!_is_mobile)
        {
            (int _width, int _height) = AntiDetectionService.GetRandomWindowSize();
            _options.AddArgument($"--window-size={_width},{_height}");
            // ! 第 2 次起不再最大化：最大化 + headless + 驱动崩溃在部分环境会互相影响
            if (_attempt == 1)
            {
                _options.AddArgument("--start-maximized");
            }
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
        }
        else
        {
            _ua = AntiDetectionService.GetRandomDesktopUserAgent();
        }
        _options.AddArgument($"--user-agent={_ua}");
        AppLogger.Debug($"本次使用 User-Agent: {_ua[..Math.Min(80, _ua.Length)]}...");

        // - 语言设置
        _options.AddArgument("--lang=zh-CN,zh,en-US,en");

        if (AppConfig.Headless)
        {
            _options.AddArgument("--headless=new");
        }

        // ! 第 2 次起关闭 GPU：显卡驱动/远程桌面环境下 GPU 进程崩溃是启动即退的常见原因
        if (_attempt >= 2)
        {
            _options.AddArgument("--disable-gpu");
            _options.AddArgument("--disable-software-rasterizer");
        }

        if (_attempt >= 3)
        {
            AppLogger.Warning("第 3 次尝试：使用备用用户目录并关闭扩展加载");
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

            AppLogger.Info($"EdgeDriver 创建成功 [Mobile={_is_mobile}, 第 {_attempt} 次尝试]，反检测已启用，页面加载超时={AppConfig.PageLoadTimeoutSeconds}秒");
            return ActiveDriver;
        }
        catch (Exception ex)
        {
            // ! 启动失败也要把 driver 进程收干净，否则残留进程会连累下一次启动
            try
            {
                ActiveDriver?.Dispose();
            }
            catch
            {
                // 忽略
            }
            ActiveDriver = null;
            AppLogger.Error("EdgeDriver 创建失败", ex);
            throw;
        }
    }

    /// <summary>
    /// 判断是否为 Edge 启动崩溃（残留进程占用用户目录的典型表现）
    /// </summary>
    private static bool IsEdgeCrash(Exception _ex)
    {
        string _msg = _ex.Message;
        return _msg.Contains("crashed")
            || _msg.Contains("DevToolsActivePort")
            || _msg.Contains("SessionNotCreated");
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
            QuitActiveDriverInternal();
        }
    }

    /// <summary>
    /// 程序退出时调用：先正常 Quit（带超时），再兜底清理残留进程与锁文件。
    /// 进程被强杀/崩溃时不会走到这里，靠下次启动的 EnsureStartupCleanup 兜底。
    /// </summary>
    public static void ShutdownAll(string _reason)
    {
        lock (Lock)
        {
            AppLogger.Info($"[{_reason}] 正在关闭浏览器并清理残留进程...");
            try
            {
                QuitActiveDriverInternal();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[{_reason}] 关闭 EdgeDriver 异常: {ex.Message}");
            }

            ProcessCleanupService.Cleanup(AppConfig.GetEdgeUserDataFullPath(), _reason);
            AppLogger.Info($"[{_reason}] 浏览器与驱动进程清理完成");
        }
    }

    /// <summary>
    /// Quit 在浏览器无响应时会阻塞很久，这里给它 10 秒，超时交给进程清理兜底
    /// </summary>
    private static void QuitActiveDriverInternal()
    {
        if (ActiveDriver is null)
        {
            return;
        }

        EdgeDriver _driver = ActiveDriver;
        ActiveDriver = null;

        try
        {
            Task _quit = Task.Run(() => _driver.Quit());
            if (!_quit.Wait(TimeSpan.FromSeconds(10)))
            {
                AppLogger.Warning("EdgeDriver.Quit 超过 10 秒未返回，改为强制结束进程");
            }
            else
            {
                AppLogger.Info("EdgeDriver 已关闭");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"关闭 EdgeDriver 时发生异常: {ex.Message}");
        }
        finally
        {
            try { _driver.Dispose(); } catch { /* 忽略 */ }
        }
    }
}
