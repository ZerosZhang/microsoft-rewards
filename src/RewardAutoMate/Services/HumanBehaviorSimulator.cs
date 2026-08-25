using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using RewardAutoMate.Logging;

namespace RewardAutoMate.Services;

/// <summary>
/// 人类行为模拟器：鼠标轨迹、自然滚动、随机点击偏移
/// </summary>
public static class HumanBehaviorSimulator
{
    private static readonly Random Random = new();

    /// <summary>
    /// 自然鼠标移动到目标元素（贝塞尔曲线轨迹）
    /// </summary>
    public static async Task MoveToElementNaturally(IWebDriver _driver, IWebElement _element, CancellationToken _ct = default)
    {
        try
        {
            Actions _actions = new Actions(_driver);

            // ? 获取元素中心位置，并添加随机偏移（不完全点击中心）
            int _offset_x = Random.Next(-10, 10);
            int _offset_y = Random.Next(-5, 5);

            // - 如果启用了复杂鼠标轨迹，使用 JavaScript 模拟
            if (Configuration.AppConfig.SimulateMouseMovement)
            {
                await SimulateBezierMouseMovement(_driver, _element, _ct);
            }
            else
            {
                _actions.MoveToElement(_element, _offset_x, _offset_y).Perform();
            }

            await Task.Delay(Random.Next(100, 500), _ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"自然鼠标移动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 使用贝塞尔曲线模拟人类鼠标移动
    /// </summary>
    private static async Task SimulateBezierMouseMovement(IWebDriver _driver, IWebElement _element, CancellationToken _ct)
    {
        try
        {
            // ! 获取当前鼠标位置和元素位置
            IJavaScriptExecutor _js = (IJavaScriptExecutor)_driver;
            Dictionary<string, object>? _current_pos = _js.ExecuteScript(
                "return { x: window.lastMouseX || window.innerWidth / 2, y: window.lastMouseY || window.innerHeight / 2 };") as Dictionary<string, object>;

            int _start_x = _current_pos != null && _current_pos.TryGetValue("x", out object? _x) ? Convert.ToInt32(_x) : 400;
            int _start_y = _current_pos != null && _current_pos.TryGetValue("y", out object? _y) ? Convert.ToInt32(_y) : 300;

            // - 获取元素位置
            Dictionary<string, object>? _rect = _js.ExecuteScript(
                "var rect = arguments[0].getBoundingClientRect(); return { x: rect.left + rect.width/2, y: rect.top + rect.height/2 };",
                _element) as Dictionary<string, object>;

            if (_rect is null)
            {
                return;
            }

            int _end_x = Convert.ToInt32(_rect["x"]) + Random.Next(-15, 15);
            int _end_y = Convert.ToInt32(_rect["y"]) + Random.Next(-10, 10);

            // - 控制点（贝塞尔曲线中间点）
            int _cp1_x = _start_x + (_end_x - _start_x) / 3 + Random.Next(-100, 100);
            int _cp1_y = _start_y + (_end_y - _start_y) / 3 + Random.Next(-50, 50);
            int _cp2_x = _start_x + 2 * (_end_x - _start_x) / 3 + Random.Next(-100, 100);
            int _cp2_y = _start_y + 2 * (_end_y - _start_y) / 3 + Random.Next(-50, 50);

            // - 生成轨迹点
            int _steps = Random.Next(15, 30);
            List<(int X, int Y)> _points = [];

            for (int i = 0; i <= _steps; i++)
            {
                double _t = (double)i / _steps;
                double _inv_t = 1 - _t;

                // 三次贝塞尔曲线
                double _px = Math.Pow(_inv_t, 3) * _start_x
                           + 3 * Math.Pow(_inv_t, 2) * _t * _cp1_x
                           + 3 * _inv_t * Math.Pow(_t, 2) * _cp2_x
                           + Math.Pow(_t, 3) * _end_x;

                double _py = Math.Pow(_inv_t, 3) * _start_y
                           + 3 * Math.Pow(_inv_t, 2) * _t * _cp1_y
                           + 3 * _inv_t * Math.Pow(_t, 2) * _cp2_y
                           + Math.Pow(_t, 3) * _end_y;

                // ? 添加轻微抖动（人类手抖）
                _px += Random.Next(-2, 2);
                _py += Random.Next(-2, 2);

                _points.Add(((int)_px, (int)_py));
            }

            // - 执行鼠标移动（每步之间有延迟，模拟人类速度变化）
            Actions _actions = new Actions(_driver);
            foreach ((int _px, int _py) in _points)
            {
                _js.ExecuteScript($"window.lastMouseX = {_px}; window.lastMouseY = {_py};");
                _js.ExecuteScript($"var evt = new MouseEvent('mousemove', {{ clientX: {_px}, clientY: {_py}, bubbles: true }}); document.dispatchEvent(evt);");

                // ? 变速移动：开始和结束慢，中间快
                int _delay = Random.Next(8, 25);
                await Task.Delay(_delay, _ct);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"贝塞尔鼠标移动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 自然滚动页面（变速滚动，随机停顿）
    /// </summary>
    public static async Task NaturalScrollPage(IWebDriver _driver, CancellationToken _ct = default)
    {
        try
        {
            IJavaScriptExecutor _js = (IJavaScriptExecutor)_driver;

            // ? 获取页面高度
            long _total_height = (long)(_js.ExecuteScript("return document.body.scrollHeight") ?? 0);
            long _viewport_height = (long)(_js.ExecuteScript("return window.innerHeight") ?? 800);
            AppLogger.Debug($"  -> 页面总高度: {_total_height}px, 视口高度: {_viewport_height}px");

            if (_total_height <= _viewport_height)
            {
                AppLogger.Debug("  -> 页面无需滚动");
                return;
            }

            // - 随机决定是否滚动到底部，还是只滚动一部分
            long _target_scroll = Random.Next(2) == 0
                ? _total_height
                : Random.Next((int)_viewport_height, (int)_total_height);
            AppLogger.Debug($"  -> 目标滚动位置: {_target_scroll}px");

            long _current_scroll = 0;
            int _step = Random.Next(100, 300);
            int _step_count = 0;
            int _read_pause_count = 0;

            while (_current_scroll < _target_scroll)
            {
                _current_scroll = Math.Min(_current_scroll + _step, _target_scroll);
                _step_count++;

                // ? 随机化每一步的滚动量（模拟人类不均匀滚动）
                _step = Random.Next(80, 400);

                // ? 偶尔反向滚动（人类回滚行为）
                if (Random.Next(10) == 0 && _current_scroll > _viewport_height)
                {
                    int _rollback_amount = Random.Next(100, 300);
                    _current_scroll -= _rollback_amount;
                    AppLogger.Debug($"    -> [回滚] 回退 {_rollback_amount}px");
                }

                _js.ExecuteScript($"window.scrollTo({{ top: {_current_scroll}, behavior: 'auto' }});");

                // - 滚动间隔随机化（变速）
                int _scroll_delay = Random.Next(50, 300);
                await Task.Delay(_scroll_delay, _ct);

                // ? 偶尔长时间停顿（阅读内容）
                if (Random.Next(8) == 0)
                {
                    int _read_pause = Random.Next(500, 2000);
                    _read_pause_count++;
                    await Task.Delay(_read_pause, _ct);
                }
            }

            // ? 滚动后可能回滚一点
            if (Random.Next(3) == 0)
            {
                long _rollback = _current_scroll - Random.Next(200, 800);
                if (_rollback > 0)
                {
                    _js.ExecuteScript($"window.scrollTo({{ top: {_rollback}, behavior: 'auto' }});");
                    await Task.Delay(Random.Next(200, 600), _ct);
                }
            }

            AppLogger.Debug($"  -> 自然滚动完成，共 {_step_count} 步，阅读停顿 {_read_pause_count} 次");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"自然滚动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 随机化点击位置（元素内随机偏移）
    /// </summary>
    public static void RandomizedClick(IWebElement _element, IWebDriver _driver)
    {
        try
        {
            // ? 使用 Actions 在元素内随机位置点击
            int _offset_x = Random.Next(-15, 15);
            int _offset_y = Random.Next(-10, 10);

            // ? 偶尔双击（人类误操作）
            if (Random.Next(50) == 0)
            {
                new Actions(_driver)
                    .MoveToElement(_element, _offset_x, _offset_y)
                    .DoubleClick()
                    .Perform();
                return;
            }

            // ? 偶尔右键（然后取消）
            if (Random.Next(100) == 0)
            {
                new Actions(_driver)
                    .MoveToElement(_element, _offset_x, _offset_y)
                    .ContextClick()
                    .Perform();
                Task.Delay(Random.Next(300, 800)).Wait();
                // 按 ESC 取消右键菜单
                new Actions(_driver)
                    .SendKeys(OpenQA.Selenium.Keys.Escape)
                    .Perform();
                return;
            }

            new Actions(_driver)
                .MoveToElement(_element, _offset_x, _offset_y)
                .Click()
                .Perform();
        }
        catch
        {
            // 降级到普通点击
            _element.Click();
        }
    }

    /// <summary>
    /// 模拟阅读行为（鼠标偶尔移动，随机停顿）
    /// </summary>
    public static async Task SimulateReading(IWebDriver _driver, int _min_seconds, int _max_seconds, CancellationToken _ct = default)
    {
        int _duration = Random.Next(_min_seconds * 1000, _max_seconds * 1000);
        int _duration_sec = _duration / 1000;
        IJavaScriptExecutor _js = (IJavaScriptExecutor)_driver;

        AppLogger.Debug($"  -> 开始模拟阅读，预计 {_duration_sec} 秒...");
        int _elapsed = 0;
        int _mouse_move_count = 0;
        while (_elapsed < _duration)
        {
            int _chunk = Random.Next(1000, 3000);
            await Task.Delay(_chunk, _ct);
            _elapsed += _chunk;

            // ? 偶尔轻微移动鼠标
            if (Random.Next(3) == 0)
            {
                try
                {
                    int _mouse_x = Random.Next(100, 800);
                    int _mouse_y = Random.Next(100, 600);
                    _js.ExecuteScript($"var evt = new MouseEvent('mousemove', {{ clientX: {_mouse_x}, clientY: {_mouse_y}, bubbles: true }}); document.dispatchEvent(evt);");
                    _mouse_move_count++;
                }
                catch
                {
                    // 忽略
                }
            }
        }
        AppLogger.Debug($"  -> 阅读模拟完成，鼠标微动 {_mouse_move_count} 次");
    }

    /// <summary>
    /// 随机化任务执行时间（避免固定模式）
    /// </summary>
    public static TimeSpan GetRandomizedInterval(TimeSpan _base_interval)
    {
        // ? 在基础间隔上随机偏移 ±20%
        double _variance = Random.NextDouble() * 0.4 - 0.2; // -20% ~ +20%
        int _new_ms = (int)(_base_interval.TotalMilliseconds * (1 + _variance));
        return TimeSpan.FromMilliseconds(_new_ms);
    }

    /// <summary>
    /// 随机化批次大小（避免每次都是固定数量）
    /// </summary>
    public static int GetRandomizedBatchSize(int _base_size)
    {
        // ? 在基础批次上随机偏移 ±2
        int _variance = Random.Next(-2, 3);
        return Math.Max(1, _base_size + _variance);
    }

    /// <summary>
    /// 随机访问一个非搜索页面（增加行为多样性）
    /// </summary>
    public static async Task VisitRandomPage(IWebDriver _driver, CancellationToken _ct = default)
    {
        string[] _pages =
        [
            "https://www.bing.com/news",
            "https://www.bing.com/images",
            "https://www.bing.com/videos",
            "https://www.msn.com",
            "https://www.bing.com/shop"
        ];

        string _page = _pages[Random.Next(_pages.Length)];
        try
        {
            AppLogger.Info($"  -> [行为多样化] 访问 {_page}");
            _driver.Navigate().GoToUrl(_page);
            int _stay_ms = Random.Next(2000, 6000);
            AppLogger.Debug($"    -> 停留 {_stay_ms / 1000.0:F1} 秒...");
            await Task.Delay(_stay_ms, _ct);
            AppLogger.Debug($"    -> 返回上一页...");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"随机访问页面失败: {ex.Message}");
        }
    }
}
