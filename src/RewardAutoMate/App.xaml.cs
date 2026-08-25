using System.Windows;
using Application = System.Windows.Application;
using RewardAutoMate.Logging;

namespace RewardAutoMate;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 先初始化日志系统（设置日志目录），确保窗口内 LogBlock 使用正确的日志路径
        AppLogger.Initialize();

        base.OnStartup(e);

        // ! 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
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
        AppLogger.CloseAndFlush();
        base.OnExit(e);
    }
}
