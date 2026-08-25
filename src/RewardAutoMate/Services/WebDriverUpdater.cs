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

            string _driver_path = Path.Combine(AppContext.BaseDirectory, "msedgedriver.exe");

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
                            AppLogger.Error("WebDriver 自动同步失败，将继续使用现有驱动", ex);
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
    /// </summary>
    private static void DownloadDriver(string _edge_version, string _target_path)
    {
        string _download_url = $"https://msedgedriver.microsoft.com/{_edge_version}/edgedriver_win64.zip";
        string _temp_dir = Path.Combine(Path.GetTempPath(), "RewardAutoMate", $"edgedriver_{_edge_version}");
        string _zip_path = Path.Combine(_temp_dir, "edgedriver_win64.zip");
        string _extract_dir = Path.Combine(_temp_dir, "extract");

        Directory.CreateDirectory(_temp_dir);

        AppLogger.Info($"正在下载 WebDriver: {_download_url}");
        using (FileStream _fs = new FileStream(_zip_path, FileMode.Create, FileAccess.Write))
        {
            using Stream _stream = HttpClient.GetStreamAsync(_download_url).GetAwaiter().GetResult();
            _stream.CopyTo(_fs);
        }

        if (Directory.Exists(_extract_dir))
        {
            Directory.Delete(_extract_dir, true);
        }
        ZipFile.ExtractToDirectory(_zip_path, _extract_dir);

        string _extracted = Path.Combine(_extract_dir, "msedgedriver.exe");
        if (!File.Exists(_extracted))
        {
            throw new FileNotFoundException("解压后未找到 msedgedriver.exe", _extracted);
        }

        File.Copy(_extracted, _target_path, true);
        AppLogger.Info($"msedgedriver 已更新到: {_target_path}");

        // 尝试同步回源码 External 目录（避免重新编译后版本回退），失败忽略
        try
        {
            string _source_path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "External", "msedgedriver.exe"));
            if (Path.GetFullPath(_source_path) != Path.GetFullPath(_target_path))
            {
                File.Copy(_extracted, _source_path, true);
                AppLogger.Info($"已同步驱动到源码目录: {_source_path}");
            }
        }
        catch
        {
            // 非开发环境忽略
        }
    }
}
