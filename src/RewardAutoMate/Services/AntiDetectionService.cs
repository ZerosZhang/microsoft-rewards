using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// 反检测服务：隐藏自动化特征、混淆浏览器指纹
/// </summary>
public static class AntiDetectionService
{
    /// <summary>
    /// 通过 CDP 执行反检测脚本，在页面加载前注入
    /// </summary>
    public static void ApplyStealthScripts(EdgeDriver _driver)
    {
        try
        {
            // ! 核心反检测：覆盖 navigator.webdriver
            string _stealth_script = """
                // 删除 webdriver 标志
                Object.defineProperty(navigator, 'webdriver', {
                    get: () => undefined,
                    configurable: true
                });

                // 删除 chrome 自动化对象
                delete window.chrome?.runtime?.OnInstalled;
                delete window.chrome?.runtime?.OnConnect;
                
                // 覆盖 permissions API
                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                    parameters.name === 'notifications' || parameters.name === 'clipboard-read' || parameters.name === 'clipboard-write'
                        ? Promise.resolve({ state: Notification.permission })
                        : originalQuery(parameters)
                );

                // 覆盖 plugins 和 mimeTypes 使其看起来真实
                Object.defineProperty(navigator, 'plugins', {
                    get: () => [
                        {name: "Chrome PDF Plugin", filename: "internal-pdf-viewer", description: "Portable Document Format"},
                        {name: "Chrome PDF Viewer", filename: "mhjfbmdgcfjbbpaeojofohoefgiehjai", description: "Portable Document Format"},
                        {name: "Native Client", filename: "internal-nacl-plugin", description: "Native Client module"}
                    ]
                });

                // 覆盖 languages
                Object.defineProperty(navigator, 'languages', {
                    get: () => ['zh-CN', 'zh', 'en-US', 'en']
                });

                // 覆盖 platform
                Object.defineProperty(navigator, 'platform', {
                    get: () => 'Win32'
                });

                // 覆盖 hardwareConcurrency
                Object.defineProperty(navigator, 'hardwareConcurrency', {
                    get: () => 8
                });

                // 覆盖 deviceMemory
                Object.defineProperty(navigator, 'deviceMemory', {
                    get: () => 8
                });

                // 覆盖 maxTouchPoints
                Object.defineProperty(navigator, 'maxTouchPoints', {
                    get: () => 0
                });

                // 伪造 Notification 权限
                if (!window.Notification) {
                    window.Notification = { permission: 'default' };
                }

                // 覆盖 Canvas 指纹相关（toDataURL 和 getImageData）
                const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
                const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
                
                const noise = () => {
                    const canvas = document.createElement('canvas');
                    const ctx = canvas.getContext('2d');
                    const imageData = ctx.createImageData(1, 1);
                    imageData.data[0] = imageData.data[0] + Math.floor(Math.random() * 2);
                    return imageData;
                };

                // 修改 Iframes 的 contentWindow 以隐藏自动化
                const originalAttachShadow = Element.prototype.attachShadow;
                Element.prototype.attachShadow = function(options) {
                    return originalAttachShadow.call(this, options);
                };

                // 覆盖 console.debug 以避免某些检测
                const originalDebug = console.debug;
                console.debug = function(...args) {
                    if (args.length > 0 && typeof args[0] === 'string' && args[0].includes('driver-evaluate')) {
                        return;
                    }
                    return originalDebug.apply(this, args);
                };

                // 修改 window.outerWidth/window.outerHeight 使其与 inner 一致
                try {
                    Object.defineProperty(window, 'outerWidth', {
                        get: () => window.innerWidth
                    });
                    Object.defineProperty(window, 'outerHeight', {
                        get: () => window.innerHeight + (window.outerHeight - window.innerHeight > 100 ? 70 : 0)
                    });
                } catch(e) {}
                """;

            _driver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object?>
            {
                ["source"] = _stealth_script
            });
            AppLogger.Debug("  -> CDP 预注入脚本已设置（Page.addScriptToEvaluateOnNewDocument）");

            // ! 执行一次当前页面注入（如果页面已加载）
            _driver.ExecuteScript(_stealth_script);
            AppLogger.Debug("  -> 当前页面反检测脚本已执行");

            AppLogger.Info("反检测脚本注入完成: navigator.webdriver=undefined, plugins伪造, Canvas/WebGL指纹已混淆");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"注入反检测脚本失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 随机化 WebGL 指纹（通过 Canvas 修改）
    /// </summary>
    public static void RandomizeWebGLFingerprint(EdgeDriver _driver)
    {
        try
        {
            string _script = """
                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {
                    if (parameter === 37445) {
                        return 'Intel Inc.';
                    }
                    if (parameter === 37446) {
                        return 'Intel Iris OpenGL Engine';
                    }
                    return getParameter(parameter);
                };
                """;
            _driver.ExecuteScript(_script);
            AppLogger.Debug("  -> WebGL 指纹已混淆 (Intel Iris OpenGL Engine)");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"WebGL 指纹随机化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 随机化 Canvas 指纹
    /// </summary>
    public static void RandomizeCanvasFingerprint(EdgeDriver _driver)
    {
        try
        {
            string _script = """
                (function() {
                    const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
                    const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
                    
                    HTMLCanvasElement.prototype.toDataURL = function(type) {
                        const ctx = this.getContext('2d');
                        if (ctx) {
                            const imageData = ctx.getImageData(0, 0, this.width, this.height);
                            for (let i = 0; i < imageData.data.length; i += 4) {
                                imageData.data[i] = Math.max(0, Math.min(255, imageData.data[i] + (Math.random() > 0.5 ? 1 : -1)));
                            }
                            ctx.putImageData(imageData, 0, 0);
                        }
                        return originalToDataURL.apply(this, arguments);
                    };
                })();
                """;
            _driver.ExecuteScript(_script);
            AppLogger.Debug("  -> Canvas 指纹已混淆 (像素级噪声注入)");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Canvas 指纹随机化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取随机的真实窗口尺寸
    /// </summary>
    public static (int Width, int Height) GetRandomWindowSize()
    {
        // ? 常见桌面分辨率
        (int, int)[] _sizes =
        [
            (1920, 1080),
            (1920, 1200),
            (2560, 1440),
            (1366, 768),
            (1440, 900),
            (1536, 864),
            (1680, 1050),
            (1280, 720)
        ];
        return _sizes[Random.Shared.Next(_sizes.Length)];
    }

    /// <summary>
    /// 获取随机化的真实 User-Agent
    /// </summary>
    public static string GetRandomDesktopUserAgent()
    {
        string[] _agents =
        [
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.0 Edg/135.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36 Edg/134.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36 Edg/133.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.0.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36 Edg/132.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:137.0) Gecko/20100101 Firefox/137.0"
        ];
        return _agents[Random.Shared.Next(_agents.Length)];
    }

    /// <summary>
    /// 获取随机化的移动端 User-Agent
    /// </summary>
    public static string GetRandomMobileUserAgent()
    {
        string[] _agents =
        [
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4.1 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_3_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.3.1 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 16_7_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (Linux; Android 14; SM-S928B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Mobile Safari/537.36 EdgA/135.0.0.0",
            "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Mobile Safari/537.36",
            "Mozilla/5.0 (iPad; CPU OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1"
        ];
        return _agents[Random.Shared.Next(_agents.Length)];
    }
}
