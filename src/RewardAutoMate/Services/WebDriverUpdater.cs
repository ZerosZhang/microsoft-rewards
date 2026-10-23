using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RewardAutoMate.Configuration;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// WebDriver 版本自动同步服务：检测本机 Edge 版本，确保 msedgedriver 版本匹配
/// </summary>
public static class WebDriverUpdater
{
    private static readonly object Lock = new object();
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    private static string? CachedDriverPath = null;

    /// <summary>
    /// 确保本地 msedgedriver 与本机 Edge 版本一致，返回驱动 exe 路径
    /// </summary>
    public static string EnsureDriverUpToDate()
    {
        lock (Lock)
        {
            if (CachedDriverPath is not null && File.Exists(CachedDriverPath))
            {
                return CachedDriverPath;
            }

            string _driver_path = ResolveDriverPath();

            if (AppConfig.AutoUpdateWebDriver)
            {
                string? _edge_version = GetEdgeVersion();
                if (!string.IsNullOrEmpty(_edge_version))
                {
                    string? _current_version = GetDriverVersion(_driver_path);
                    if (_current_version != _edge_version)
                    {
                        AppLogger.Info($"检测到 Edge 版本: {_edge_version}，本地驱动版本: {(_current_version ?? "不存在")}，准备同步 WebDriver...");
                        try
                        {
                            DownloadDriver(_edge_version, _driver_path);
                            AppLogger.Info($"WebDriver 已同步到版本 {_edge_version}");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("WebDriver 自动同步失败", ex);

                            if (!File.Exists(_driver_path))
                            {
                                // ! 本地没有驱动时不能再"继续使用现有驱动"，否则后面只会得到更晦涩的报错
                                throw new InvalidOperationException(
                                    $"本机缺少 msedgedriver，且自动下载失败，无法启动浏览器。请手动把 {_edge_version} 版驱动放到 {_driver_path}。原因: {ex.Message}",
                                    ex);
                            }

                            AppLogger.Warning($"继续使用本地驱动 {_current_version ?? "未知版本"}（与本机 Edge {_edge_version} 不一致，浏览器可能无法启动）");
                        }
                    }
                    else
                    {
                        AppLogger.Debug($"WebDriver 版本匹配: {_edge_version}");
                    }
                }
                else
                {
                    AppLogger.Warning("无法获取 Edge 浏览器版本，跳过 WebDriver 同步");
                }
            }

            CachedDriverPath = _driver_path;
            return _driver_path;
        }
    }

    /// <summary>
    /// 定位驱动文件：优先运行目录根下的 msedgedriver.exe，
    /// 其次发布产物自带的 External\msedgedriver.exe（dotnet publish 只会把它放在 External 子目录）
    /// </summary>
    private static string ResolveDriverPath()
    {
        string _root_path = Path.Combine(AppContext.BaseDirectory, "msedgedriver.exe");
        if (File.Exists(_root_path))
        {
            return _root_path;
        }

        string _external_path = Path.Combine(AppContext.BaseDirectory, "External", "msedgedriver.exe");
        if (File.Exists(_external_path))
        {
            AppLogger.Info($"运行目录没有驱动，改用发布产物自带的: {_external_path}");
            return _external_path;
        }

        return _root_path;
    }

    /// <summary>
    /// 获取本机 Edge 浏览器版本（注册表优先，回退到文件版本）
    /// </summary>
    private static string? GetEdgeVersion()
    {
        string[] _registry_paths =
        [
            @"SOFTWARE\WOW6432Node\Microsoft\Edge\BLBeacon",
            @"SOFTWARE\Microsoft\Edge\BLBeacon"
        ];
        foreach (string _path in _registry_paths)
        {
            try
            {
                using RegistryKey? _key = Registry.LocalMachine.OpenSubKey(_path);
                if (_key?.GetValue("version") is string _version && !string.IsNullOrEmpty(_version))
                {
                    return _version;
                }
            }
            catch
            {
                // 忽略单个注册表路径失败
            }
        }

        string[] _edge_paths =
        [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        ];
        foreach (string _path in _edge_paths)
        {
            try
            {
                if (File.Exists(_path))
                {
                    FileVersionInfo _info = FileVersionInfo.GetVersionInfo(_path);
                    if (!string.IsNullOrEmpty(_info.ProductVersion))
                    {
                        return _info.ProductVersion;
                    }
                }
            }
            catch
            {
                // 忽略单个路径失败
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 msedgedriver 的版本号（通过 --version 输出）
    /// </summary>
    private static string? GetDriverVersion(string _driver_path)
    {
        if (!File.Exists(_driver_path))
        {
            return null;
        }

        try
        {
            using Process _process = new Process();
            _process.StartInfo.FileName = _driver_path;
            _process.StartInfo.Arguments = "--version";
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.CreateNoWindow = true;

            if (!_process.Start())
            {
                return null;
            }

            string _output = _process.StandardOutput.ReadToEnd();
            _process.WaitForExit(5000);

            // 输出格式: "Microsoft Edge WebDriver 151.0.4129.101 (...)"
            Match _match = Regex.Match(_output, @"(\d+\.\d+\.\d+\.\d+)");
            return _match.Success ? _match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从微软官方源下载并解压匹配版本的 msedgedriver，替换目标文件
    /// ! 先下到 .tmp 并校验是合法 zip，再覆盖；覆盖前清理残留驱动进程
    /// </summary>
    private static void DownloadDriver(string _edge_version, string _target_path)
    {
        string _download_url = $"https://msedgedriver.microsoft.com/{_edge_version}/edgedriver_win64.zip";
        string _temp_dir = Path.Combine(Path.GetTempPath(), "RewardAutoMate", $"edgedriver_{_edge_version}");
        string _zip_path = Path.Combine(_temp_dir, "edgedriver_win64.zip");
        string _zip_tmp = _zip_path + ".tmp";
        string _extract_dir = Path.Combine(_temp_dir, "extract");

        Directory.CreateDirectory(_temp_dir);

        AppLogger.Info($"正在下载 WebDriver: {_download_url}");
        DownloadWithRetry(_download_url, _zip_tmp, 3);

        // ! 校验压缩包完整（下载中断/被代理劫持时这里能提前发现）
        try
        {
            using ZipArchive _archive = ZipFile.OpenRead(_zip_tmp);
            if (_archive.Entries.All(_entry => _entry.FullName != "msedgedriver.exe"))
            {
                throw new InvalidDataException("压缩包内没有 msedgedriver.exe");
            }
        }
        catch (Exception ex)
        {
            TryDelete(_zip_tmp);
            throw new InvalidDataException($"下载的驱动包无效: {ex.Message}", ex);
        }

        File.Move(_zip_tmp, _zip_path, true);

        if (Directory.Exists(_extract_dir))
        {
            TryDeleteDirectory(_extract_dir);
        }
        ZipFile.ExtractToDirectory(_zip_path, _extract_dir);

        string _extracted = Path.Combine(_extract_dir, "msedgedriver.exe");
        if (!File.Exists(_extracted))
        {
            throw new FileNotFoundException("解压后未找到 msedgedriver.exe", _extracted);
        }

        // ! 覆盖前先结束残留 msedgedriver 进程，否则文件被占用会导致 Copy 失败
        ProcessCleanupService.Cleanup(null, "驱动升级");

        CopyWithRetry(_extracted, _target_path, 3);
        AppLogger.Info($"msedgedriver 已更新到: {_target_path}");

        // 尝试同步回源码 External 目录（避免重新编译后版本回退），失败忽略
        try
        {
            string _source_path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "External", "msedgedriver.exe"));
            if (Path.GetFullPath(_source_path) != Path.GetFullPath(_target_path))
            {
                CopyWithRetry(_extracted, _source_path, 2);
                AppLogger.Info($"已同步驱动到源码目录: {_source_path}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"同步驱动到源码目录失败（非开发环境可忽略）: {ex.Message}");
        }
    }

    private static void DownloadWithRetry(string _url, string _target, int _max_attempts)
    {
        for (int _attempt = 1; _attempt <= _max_attempts; _attempt++)
        {
            try
            {
                using FileStream _fs = new FileStream(_target, FileMode.Create, FileAccess.Write, FileShare.None);
                using Stream _stream = HttpClient.GetStreamAsync(_url).GetAwaiter().GetResult();
                _stream.CopyTo(_fs);
                AppLogger.Info($"驱动下载完成: {_fs.Length / 1024 / 1024.0:F1} MB（第 {_attempt} 次尝试）");
                return;
            }
            catch (Exception ex)
            {
                TryDelete(_target);
                if (_attempt == _max_attempts)
                {
                    throw new IOException($"下载驱动失败（已重试 {_max_attempts} 次）: {ex.Message}", ex);
                }
                AppLogger.Warning($"驱动下载失败（第 {_attempt}/{_max_attempts} 次）: {ex.Message}，3 秒后重试");
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
    }

    private static void CopyWithRetry(string _source, string _target, int _max_attempts)
    {
        for (int _attempt = 1; _attempt <= _max_attempts; _attempt++)
        {
            try
            {
                File.Copy(_source, _target, true);
                return;
            }
            catch (Exception ex) when (_attempt < _max_attempts)
            {
                AppLogger.Warning($"覆盖驱动失败（第 {_attempt}/{_max_attempts} 次，可能被进程占用）: {ex.Message}，2 秒后重试");
                ProcessCleanupService.Cleanup(null, "驱动覆盖重试");
            }
        }

        // 最后一次不再吞异常，交给上层记录
        File.Copy(_source, _target, true);
    }

    private static void TryDelete(string _path)
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // 清理失败忽略
        }
    }

    private static void TryDeleteDirectory(string _path)
    {
        try
        {
            Directory.Delete(_path, true);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"清理临时解压目录失败: {ex.Message}");
        }
    }
}
