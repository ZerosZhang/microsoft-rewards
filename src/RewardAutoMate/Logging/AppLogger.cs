using System.IO;
using BaseTool;

namespace RewardAutoMate.Logging;

/// <summary>
/// 日志封装类：基于 BaseTool 的 BaseLogManager，统一代码调用入口
/// </summary>
/// <remarks>
/// 级别开关（BaseLogManager.DebugEnable 等）只影响界面显示，文件始终完整记录。
/// 界面过滤规则见 <see cref="BaseLogManager.DebugEnable"/>（默认关闭 Debug 界面显示）。
/// </remarks>
public static class AppLogger
{
    private static bool IsInitialized = false;

    static AppLogger()
    {
        Initialize();
    }

    /// <summary>
    /// 初始化日志系统（设置目录并注册文件输出），需在创建界面（LogBlock）之前调用
    /// </summary>
    public static void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }
        IsInitialized = true;

        // 日志目录统一放 Logs（文件按天分文件自动保留 30 天；SQLite 日志库一并归入）
        string _log_dir = Path.Combine(AppContext.BaseDirectory, "Logs");
        BaseDefine.DirectoryPathOnSaveLog = _log_dir;
        BaseDefine.SQLiteFilePathOnSaveLog = Path.Combine(_log_dir, "LogOnSaveSQLite.db");
        BaseLogManager.Register_SaveLogToFile();

        // Debug 只写文件，不显示在界面（LogBlock / 状态回调）
        BaseLogManager.DebugEnable = false;
    }

    public static void Debug(string _message)
    {
        BaseLogManager.SendLog(LogLevel.Debug, _message, "", false);
    }

    public static void Info(string _message)
    {
        BaseLogManager.SendLog(LogLevel.Info, _message, "", true);
    }

    public static void Warning(string _message)
    {
        BaseLogManager.SendLog(LogLevel.Warning, _message, "", true);
    }

    public static void Error(string _message, Exception? _exception = null)
    {
        BaseLogManager.SendLog(LogLevel.Error, _message, _exception?.ToString() ?? "", true);
    }

    public static void Fatal(string _message, Exception? _exception = null)
    {
        BaseLogManager.SendLog(LogLevel.Error, _message, _exception?.ToString() ?? "", true);
    }

    /// <summary>
    /// 关闭日志文件流与数据库连接
    /// </summary>
    public static void CloseAndFlush()
    {
        BaseLogManager.Close();
    }
}
