using System.Windows;
using Application = System.Windows.Application;
using RewardAutoMate.Logging;

namespace RewardAutoMate;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // ! 单实例：避免同时起两个进程互相清理/抢占同一个 Edge profile（这正是残留进程与启动崩溃的常见来源）
    private Mutex? SingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        SingleInstanceMutex = new Mutex(true, @"Local\RewardAutoMate_SingleInstance", out bool _created_new);
        if (!_created_new)
        {
            System.Windows.MessageBox.Show("RewardAutoMate 已在运行（可能在屏幕右下角托盘里）。\n如需重启，请先在托盘图标上右键选择「退出」。",
                "程序已在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 先初始化日志系统（设置日志目录），确保窗口内 LogBlock 使用正确的日志路径
        AppLogger.Initialize();

        base.OnStartup(e);

        // ! 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// 进程退出兜底（OnExit 没走到的场景）：尽最大努力收掉浏览器与驱动进程
    /// </summary>
    private static void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        try
        {
            Services.EdgeDriverFactory.ShutdownAll("进程退出");
        }
        catch
        {
            // 退出阶段不再抛异常
        }
    }

    /// <summary>
    /// 注销/关机时也走一遍清理，避免系统强杀进程后留下孤儿浏览器（下次启动会因 profile 被占用而崩溃）
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        AppLogger.Info("系统注销/关机，正在清理浏览器进程");
        try
        {
            Services.EdgeDriverFactory.ShutdownAll("系统注销");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"系统注销清理异常: {ex.Message}");
        }
        AppLogger.CloseAndFlush();
        base.OnSessionEnding(e);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception? _ex = e.ExceptionObject as Exception;
        AppLogger.Fatal("未处理的全局异常", _ex);

        // ! 尝试发送邮件通知
        try
        {
            Services.EmailNotificationService.SendExceptionNotificationAsync(
                "UnhandledException",
                _ex?.Message ?? "未知错误",
                _ex?.ToString() ?? "无堆栈",
                "程序崩溃",
                []).Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // 忽略邮件发送失败
        }

        AppLogger.CloseAndFlush();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Fatal("UI 线程未处理异常", e.Exception);

        try
        {
            Services.EmailNotificationService.SendExceptionNotificationAsync(
                "DispatcherUnhandledException",
                e.Exception.Message,
                e.Exception.ToString(),
                "UI 线程异常",
                []).Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // 忽略
        }

        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("未观察到的任务异常", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("RewardAutoMate 关闭");
        try
        {
            // ! 关键：正常退出时也要 Quit 驱动并收掉 msedge/msedgedriver，
            //   否则进程结束后浏览器仍占着 EdgeUserData，下次启动必定崩溃
            Services.EdgeDriverFactory.ShutdownAll("程序退出");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"退出清理异常: {ex.Message}");
        }

        SingleInstanceMutex?.ReleaseMutex();
        SingleInstanceMutex?.Dispose();
        SingleInstanceMutex = null;

        AppLogger.CloseAndFlush();
        base.OnExit(e);
    }
}
