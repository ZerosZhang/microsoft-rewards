using System.Diagnostics;
using System.IO;
using System.Management;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// 浏览器进程清理：只处理本程序自己的 msedgedriver / 使用项目 profile 的 msedge，
/// 不碰用户自己开的浏览器窗口（靠命令行里的 --user-data-dir 精确匹配）
/// </summary>
public static class ProcessCleanupService
{
    private static readonly object Lock = new object();

    /// <summary>
    /// 清理指定 profile 下的残留进程与锁文件。
    /// 返回 (结束的浏览器进程数, 结束的驱动进程数)
    /// </summary>
    public static (int browsers, int drivers) Cleanup(string? _user_data_dir, string _reason)
    {
        lock (Lock)
        {
            int _browsers = string.IsNullOrEmpty(_user_data_dir) ? 0 : KillBrowsersForProfile(_user_data_dir);
            int _drivers = KillDriverProcesses();

            if (_browsers > 0 || _drivers > 0)
            {
                AppLogger.Warning($"[进程清理/{_reason}] 结束了 {_browsers} 个浏览器进程、{_drivers} 个驱动进程");
                // ! 给系统释放文件句柄/端口的时间，否则紧接着启动大概率还是崩
                Thread.Sleep(1200);
            }

            RemoveStaleLockFiles(_user_data_dir);
            return (_browsers, _drivers);
        }
    }

    /// <summary>
    /// 只结束命令行里带本项目 user-data-dir 的 msedge 进程。
    /// 调用方未提供目录时不做任何浏览器清理（宁可不杀，也不误杀用户正在用的浏览器）；
    /// WMI 不可用时同样放弃清理。
    /// </summary>
    private static int KillBrowsersForProfile(string? _user_data_dir)
    {
        if (string.IsNullOrEmpty(_user_data_dir))
        {
            return 0;
        }

        List<(int pid, string? cmdline)> _targets = [];

        try
        {
            using ManagementObjectSearcher _searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe'");
            foreach (ManagementObject _mo in _searcher.Get().Cast<ManagementObject>())
            {
                try
                {
                    int _pid = Convert.ToInt32(_mo["ProcessId"]);
                    string? _cmdline = _mo["CommandLine"] as string;
                    if (_cmdline is null || !_cmdline.Contains(_user_data_dir, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    _targets.Add((_pid, _cmdline));
                }
                catch
                {
                    // 进程可能在枚举期间退出，忽略
                }
                finally
                {
                    _mo.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[进程清理] 枚举 msedge 进程失败（跳过浏览器清理）: {ex.Message}");
            return 0;
        }

        int _killed = 0;
        foreach ((int _pid, string? _cmdline) in _targets)
        {
            try
            {
                using Process _process = Process.GetProcessById(_pid);
                string _hint = _cmdline is not null && _cmdline.Contains("--headless", StringComparison.OrdinalIgnoreCase)
                    ? "headless"
                    : "有界面";
                _process.Kill(true);
                _killed++;
                AppLogger.Info($"[进程清理] 已结束残留浏览器进程 PID={_pid}（{_hint}）");
            }
            catch (ArgumentException)
            {
                // 进程已不存在
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[进程清理] 结束浏览器进程 PID={_pid} 失败: {ex.Message}");
            }
        }

        return _killed;
    }

    /// <summary>
    /// 结束所有 msedgedriver 进程。Selenium 之外几乎没有程序会常驻它，
    /// 残留驱动会占用驱动 exe 与调试端口，导致下次启动/覆盖升级失败。
    /// </summary>
    private static int KillDriverProcesses()
    {
        int _killed = 0;
        Process[] _drivers;
        try
        {
            _drivers = Process.GetProcessesByName("msedgedriver");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[进程清理] 枚举 msedgedriver 进程失败: {ex.Message}");
            return 0;
        }

        foreach (Process _process in _drivers)
        {
            try
            {
                int _pid = _process.Id;
                _process.Kill(true);
                if (_process.WaitForExit(3000))
                {
                    _killed++;
                    AppLogger.Info($"[进程清理] 已结束残留驱动进程 PID={_pid}");
                }
                else
                {
                    AppLogger.Warning($"[进程清理] 驱动进程 PID={_pid} 未在 3 秒内退出");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[进程清理] 结束驱动进程失败: {ex.Message}");
            }
            finally
            {
                _process.Dispose();
            }
        }

        return _killed;
    }

    /// <summary>
    /// 删除上次异常退出留下的锁文件（Chromium 的部分锁在进程被杀后不会自动清理）
    /// </summary>
    private static void RemoveStaleLockFiles(string? _user_data_dir)
    {
        if (string.IsNullOrEmpty(_user_data_dir) || !Directory.Exists(_user_data_dir))
        {
            return;
        }

        string[] _lock_names = ["SingletonLock", "SingletonSocket", "SingletonCookie", "DevToolsActivePort", "lockfile"];
        foreach (string _name in _lock_names)
        {
            string _path = Path.Combine(_user_data_dir, _name);
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                    AppLogger.Info($"[进程清理] 已删除残留锁文件: {_name}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[进程清理] 删除锁文件 {_name} 失败: {ex.Message}");
            }
        }
    }
}
