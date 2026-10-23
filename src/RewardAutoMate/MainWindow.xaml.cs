using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Application = System.Windows.Application;
using BaseTool;
using Color = System.Windows.Media.Color;
using Timer = System.Threading.Timer;
using ToolTip = System.Windows.Controls.ToolTip;
using RewardAutoMate.Logging;
using RewardAutoMate.Models;
using RewardAutoMate.Services;
using OpenQA.Selenium.Edge;

namespace RewardAutoMate;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int Rows = 7;
    private const int Columns = 52;
    [AllowNull]
    private List<QueueItem> HeatmapData = [];
    readonly NotifyIcon NotifyIcon = new NotifyIcon();

    private readonly Timer MainTimer;
    private readonly Timer WeeklyReportTimer;
    private DateTime NextExecuteTime = DateTime.Now;
    private bool IsExecuting = false;

    // ! 串行化所有浏览器操作（启动刷新 / 主流程），避免并发创建 EdgeDriver 会话互相 Quit
    private static readonly SemaphoreSlim BrowserLock = new SemaphoreSlim(1, 1);

    // ! 今日统计
    private TodayStats TodayStats = new TodayStats();

    public MainWindow()
    {
        InitializeComponent();

        // ! 将完整日志视图挂载到日志区域（LogBlock 构造时已自动订阅 BaseLogManager 日志）
        LogViewHost.Content = LogBlock.LogView;

        // ! 订阅日志用于更新运行状态
        BaseLogManager.Register_SendLogFunction(OnStatusLog);

        // ! 初始化日志
        AppLogger.Info("==== RewardAutoMate 启动 ====");
        AppLogger.Info($"版本: .NET 8 WPF");
        AppLogger.Info($"工作目录: {AppContext.BaseDirectory}");

        NotifyIcon.BalloonTipText = "设置界面已隐藏";
        NotifyIcon.Icon = new Icon("RewardAutoMate.ico");
        NotifyIcon.Visible = true;
        NotifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        ContextMenuStrip _menu = new ContextMenuStrip();
        NotifyIcon.ContextMenuStrip = _menu;

        ToolStripMenuItem _item_exit = new ToolStripMenuItem() { Text = "退出" };
        _item_exit.Click += (_, _) =>
        {
            AppLogger.Info("用户点击退出");
            AppLogger.CloseAndFlush();
            Application.Current.Shutdown();
        };
        _menu.Items.Add(_item_exit);

        HeatmapData = QueueService.DequeueTop365();
        AppLogger.Info($"热力图数据加载完成，共 {HeatmapData.Count} 条记录");

        CreateHeatmap();
        UpdateHeatmap();
        AppLogger.Info("热力图渲染完成");

        // ! 初始化统计面板
        InitTodayStats();
        UpdateStatsDisplay();

        // ! 启动时显示状态
        TextBlock_CurrentPoints.Text = "读取中...";
        TextBlock_Level.Text = "读取中...";

        // ! 启动时尝试读取 Dashboard 状态（后台；与主流程用信号量串行，避免并发浏览器会话）
        _ = Task.Run(async () => await RefreshDashboardStatsAsync());

        // ! 启动定时器（首次执行延迟 30 秒，避免启动时立即执行导致界面卡顿）
        TimeSpan _interval = TimeSpan.FromHours(Configuration.AppConfig.ExecuteIntervalHours);
        NextExecuteTime = DateTime.Now.Add(TimeSpan.FromSeconds(30));
        MainTimer = new Timer(async (_) => await AutoSearchFunction(), null, TimeSpan.FromSeconds(30), _interval);

        AppLogger.Info($"定时器已启动，执行间隔: {_interval.TotalHours:F1} 小时");
        AppLogger.Info($"预计下次执行: {NextExecuteTime:yyyy-MM-dd HH:mm:ss}");
        UpdateNextRunDisplay();

        // ! 周报定时器（每周一 09:00 发送执行汇总）
        WeeklyReportTimer = new Timer(async (_) => await SendWeeklyReportAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ScheduleWeeklyReportTimer();
    }

    #region 周报定时调度

    /// <summary>
    /// 调度周报定时器：周一 09:00 触发；若在周一 09:00 之后启动且本周未发，则短暂延迟后补发本周
    /// </summary>
    private void ScheduleWeeklyReportTimer()
    {
        DateTime _now = DateTime.Now;
        TimeSpan _delay;
        bool _catch_up = _now.DayOfWeek == DayOfWeek.Monday
                         && _now.TimeOfDay >= TimeSpan.FromHours(9)
                         && !ExecutionHistoryService.IsWeeklyReportSent();

        if (_catch_up)
        {
            _delay = TimeSpan.FromSeconds(10);
            AppLogger.Info("周报定时器已调度，当前为周一 09:00 后且本周未发，10 秒后补发本周周报");
        }
        else
        {
            DateTime _next = GetNextMonday9AM(_now);
            _delay = _next - _now;
            AppLogger.Info($"周报定时器已调度，下次发送: {_next:yyyy-MM-dd HH:mm:ss}");
        }

        WeeklyReportTimer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 计算下一个周一 09:00（若目标时刻已过则顺延一周）
    /// </summary>
    private static DateTime GetNextMonday9AM(DateTime _now)
    {
        int _days_to_monday = ((int)DayOfWeek.Monday - (int)_now.DayOfWeek + 7) % 7;
        DateTime _next = _now.Date.AddDays(_days_to_monday).AddHours(9);
        if (_next <= _now)
        {
            _next = _next.AddDays(7);
        }
        return _next;
    }

    /// <summary>
    /// 发送本周执行汇总邮件，发送后重新调度下周一
    /// </summary>
    private async Task SendWeeklyReportAsync()
    {
        try
        {
            if (!Configuration.AppConfig.SendWeeklyReport)
            {
                AppLogger.Info("周报已禁用（SendWeeklyReport=false），跳过");
                return;
            }

            if (ExecutionHistoryService.IsWeeklyReportSent())
            {
                AppLogger.Info("本周周报已发送，跳过");
                return;
            }

            AppLogger.Info("正在生成并发送本周执行汇总邮件...");
            await EmailNotificationService.SendWeeklySummaryAsync();
            ExecutionHistoryService.MarkWeeklyReportSent();
            AppLogger.Info("本周周报已发送");
        }
        catch (Exception ex)
        {
            AppLogger.Error("发送周报异常", ex);
        }
        finally
        {
            ScheduleWeeklyReportTimer();
        }
    }

    #endregion

    #region 实时日志处理

    /// <summary>
    /// BaseLogManager 日志回调，用于更新运行状态（界面日志列表由 LogBlock 负责显示）
    /// </summary>
    private void OnStatusLog(Log _log)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string _message = _log.LogMessage;

                if (_message.Contains("Phase 1/4"))
                    TextBlock_LogStatus.Text = "正在执行 PC 端搜索...";
                else if (_message.Contains("Phase 2/4"))
                    TextBlock_LogStatus.Text = "正在执行移动端搜索...";
                else if (_message.Contains("Phase 3/4"))
                    TextBlock_LogStatus.Text = "正在执行 Dashboard 任务...";
                else if (_message.Contains("Phase 4/4"))
                    TextBlock_LogStatus.Text = "正在生成执行报告...";
                else if (_message.Contains("本次执行全部完成"))
                    TextBlock_LogStatus.Text = "执行完成，等待下次定时触发";
                else if ((_message.Contains("异常") || _message.Contains("失败")) && _log.LogLevel >= LogLevel.Error)
                    TextBlock_LogStatus.Text = $"异常: {_message[..Math.Min(30, _message.Length)]}";
                else if (_message.Contains("暂停"))
                    TextBlock_LogStatus.Text = _message[..Math.Min(40, _message.Length)];
            }));
        }
        catch { }
    }

    #endregion

    #region 今日统计

    private void InitTodayStats()
    {
        TodayStats = new TodayStats
        {
            PcSearchesTarget = Configuration.AppConfig.MaxPcSearches,
            MobileSearchesTarget = Configuration.AppConfig.MaxMobileSearches,
            DailySetTotal = 3, // ? Daily Set 通常是 3 项
            NextExecuteTime = NextExecuteTime,
            LastUpdateTime = DateTime.Now
        };
    }

    /// <summary>
    /// 从 Dashboard 读取今日统计（后台执行）
    /// </summary>
    private async Task RefreshDashboardStatsAsync()
    {
        if (!Configuration.AppConfig.EnableDashboardTasks)
        {
            AppLogger.Info("Dashboard 任务已禁用，跳过状态刷新");
            return;
        }

        AppLogger.Info("正在从 Dashboard 刷新今日统计...");
        EdgeDriver? _driver = null;

        // ! 串行化：等待浏览器锁（最多 30 秒），拿不到说明有其他操作占用，放弃本次刷新
        if (!await BrowserLock.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            AppLogger.Warning("等待浏览器操作完成超时，跳过本次统计刷新");
            return;
        }
        try
        {
            _driver = EdgeDriverFactory.CreateDriver(false);
            RewardsDashboardService _dashboard = new RewardsDashboardService(_driver);

            // - 读取 Dashboard 状态
            _dashboard.NavigateToDashboard();
            await Task.Delay(2000);

            // ! 获取积分和等级
            DashboardStatus _status = _dashboard.GetDashboardStatus();
            TodayStats.CurrentPoints = _status.CurrentPoints;
            TodayStats.Level = _status.Level;

            // ? 初始化今日起始积分（用于计算今日实际获得）
            // 每天第一次成功读取时记录，跨天自动重置
            if (TodayStats.TodayStartPoints == 0 || TodayStats.LastUpdateTime.Date != DateTime.Today)
            {
                TodayStats.TodayStartPoints = _status.CurrentPoints;
                AppLogger.Info($"记录今日起始积分: {TodayStats.TodayStartPoints:N0}，今日已获得将从该值计算");
            }

            // ! 获取今日进度（搜索、活动、签到、DailySet、可领取）
            TodayProgress _progress = _dashboard.GetTodayProgress();
            TodayStats.ClaimableCount = _progress.ClaimableCount;
            TodayStats.DailySetDone = _progress.DailySetDone;
            TodayStats.DailySetTotal = _progress.DailySetTotal;

            // ? 保存 Dashboard 任务进度（用于显示页面上的任务状态）
            TodayStats.DashboardSearchDone = _progress.SearchProgress.Done;
            TodayStats.DashboardSearchTotal = _progress.SearchProgress.Total;
            TodayStats.DashboardActivityDone = _progress.ActivityProgress.Done;
            TodayStats.DashboardActivityTotal = _progress.ActivityProgress.Total;
            TodayStats.DashboardCheckInDone = _progress.CheckInProgress.Done;
            TodayStats.DashboardCheckInTotal = _progress.CheckInProgress.Total;

            // ! 注意：PcSearchesDone / MobileSearchesDone 保留本程序实际执行计数
            // Dashboard 上的"搜索: 1/1"是微软任务进度，不是本程序搜索次数

            TodayStats.LastUpdateTime = DateTime.Now;

            // - 更新 UI
            Dispatcher.Invoke(UpdateStatsDisplay);
            AppLogger.Info($"Dashboard 状态刷新完成: 积分={TodayStats.CurrentPoints:N0}, 等级={TodayStats.Level}, DailySet={TodayStats.DailySetDone}/{TodayStats.DailySetTotal}, 可领取={TodayStats.ClaimableCount}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"刷新 Dashboard 状态失败: {ex.Message}");
        }
        finally
        {
            BrowserLock.Release();
            EdgeDriverFactory.QuitActiveDriver();
        }
    }

    private void UpdateStatsDisplay()
    {
        try
        {
            TextBlock_CurrentPoints.Text = TodayStats.CurrentPoints > 0 ? TodayStats.CurrentPoints.ToString("N0") : "--";
            TextBlock_Claimable.Text = TodayStats.ClaimableCount >= 0 ? TodayStats.ClaimableCount.ToString() : "--";
            TextBlock_TodayEarned.Text = $"+{TodayStats.TodayEarnedPoints:N0}";
            TextBlock_Level.Text = string.IsNullOrEmpty(TodayStats.Level) ? "--" : TodayStats.Level;

            // 本程序搜索进度
            TextBlock_PcSearch.Text = $"{TodayStats.PcSearchesDone} / {TodayStats.PcSearchesTarget}";
            ProgressBar_PcSearch.Value = TodayStats.PcSearchPercent;

            TextBlock_MobileSearch.Text = $"{TodayStats.MobileSearchesDone} / {TodayStats.MobileSearchesTarget}";
            ProgressBar_MobileSearch.Value = TodayStats.MobileSearchPercent;

            // Dashboard 任务状态
            TextBlock_DailySet.Text = TodayStats.DailySetTotal > 0
                ? $"{TodayStats.DailySetDone} / {TodayStats.DailySetTotal}"
                : "--";
            TextBlock_DashboardSearch.Text = TodayStats.DashboardSearchTotal > 0
                ? $"{TodayStats.DashboardSearchDone} / {TodayStats.DashboardSearchTotal}"
                : "--";
            TextBlock_DashboardActivity.Text = TodayStats.DashboardActivityTotal > 0
                ? $"{TodayStats.DashboardActivityDone} / {TodayStats.DashboardActivityTotal}"
                : "--";
            TextBlock_DashboardCheckIn.Text = TodayStats.DashboardCheckInTotal > 0
                ? $"{TodayStats.DashboardCheckInDone} / {TodayStats.DashboardCheckInTotal}"
                : "--";

            // 本程序执行统计
            TextBlock_Benefits.Text = TodayStats.BenefitsClaimed.ToString();
            TextBlock_PunchCards.Text = TodayStats.PunchCardsDone.ToString();
            TextBlock_DailyTasks.Text = TodayStats.DailyTasksDone.ToString();

            UpdateNextRunDisplay();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"更新统计面板失败: {ex.Message}");
        }
    }

    private void UpdateNextRunDisplay()
    {
        try
        {
            if (NextExecuteTime.Date == DateTime.Today)
            {
                TextBlock_NextRun.Text = NextExecuteTime.ToString("HH:mm:ss");
            }
            else
            {
                TextBlock_NextRun.Text = NextExecuteTime.ToString("MM-dd HH:mm");
            }
        }
        catch { }
    }

    #endregion

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        Show();
    }

    private async Task AutoSearchFunction()
    {
        // ! 防重入：上一轮未完成时跳过本次触发，避免并发创建浏览器导致会话冲突
        if (IsExecuting)
        {
            AppLogger.Warning("上一轮执行尚未完成，跳过本次定时触发");
            return;
        }
        IsExecuting = true;

        try
        {
            // ! 串行化：等待启动刷新释放浏览器锁，最多 5 分钟；超时则跳过本轮，避免永久死锁
            if (!await BrowserLock.WaitAsync(TimeSpan.FromMinutes(5)))
            {
                AppLogger.Warning("等待浏览器操作完成超时，跳过本次定时触发");
                return;
            }

            try
            {
                AppLogger.Info("========================================");
                AppLogger.Info("⏰ 定时器触发，开始自动执行流程");
                AppLogger.Info($"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                AppLogger.Info("========================================");

                // ! 实时进度回调
                var _pc_progress = new Progress<(int done, int total)>(_tuple =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TodayStats.PcSearchesDone = _tuple.done;
                        TodayStats.PcSearchesTarget = _tuple.total;
                        UpdateStatsDisplay();
                    });
                });
                var _mobile_progress = new Progress<(int done, int total)>(_tuple =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TodayStats.MobileSearchesDone = _tuple.done;
                        TodayStats.MobileSearchesTarget = _tuple.total;
                        UpdateStatsDisplay();
                    });
                });

                ExecuteResult _result = await WindowsPCServer.ExecuteFullRewardsFlow(_pc_progress, _mobile_progress);

                // - 更新统计（最终值，防止进度回调和最终结果不一致）
                TodayStats.PcSearchesDone = _result.PcSearches;
                TodayStats.MobileSearchesDone = _result.MobileSearches;
                TodayStats.BenefitsClaimed += _result.BenefitsClaimed;
                TodayStats.PunchCardsDone += _result.PunchCardsCompleted;
                TodayStats.DailyTasksDone += _result.DailyTasksCompleted;
                TodayStats.LastUpdateTime = DateTime.Now;
                Dispatcher.Invoke(UpdateStatsDisplay);

                // - 更新热力图数据
                QueueItem _item = QueueService.GetTodayCount();
                _item.Count += (uint)_result.TotalSearches;
                QueueService.UpdateItem(_item);
                HeatmapData = QueueService.DequeueTop365();
                Dispatcher.Invoke(UpdateHeatmap);

                NextExecuteTime = DateTime.Now.AddHours(Configuration.AppConfig.ExecuteIntervalHours);
                AppLogger.Info("热力图已更新");
                AppLogger.Info($"下次执行时间: {NextExecuteTime:yyyy-MM-dd HH:mm:ss}");
                Dispatcher.Invoke(UpdateNextRunDisplay);
            }
            catch (Exception ex)
            {
                AppLogger.Error("自动执行流程异常", ex);
            }
            finally
            {
                BrowserLock.Release();
            }

            // ! 主流程锁已释放，再刷新 Dashboard 统计（刷新内部自行取锁，避免嵌套锁死）
            await RefreshDashboardStatsAsync();
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void CreateHeatmap()
    {
        for (int i = 0; i < Rows; i++)
        {
            HeatmapGrid.RowDefinitions.Add(new RowDefinition());
        }
        for (int j = 0; j < Columns; j++)
        {
            HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j < Columns; j++)
            {
                Border border = new Border
                {
                    Width = 10,
                    Height = 10,
                    Margin = new Thickness(2),
                };
                Grid.SetRow(border, i);
                Grid.SetColumn(border, j);

                HeatmapGrid.Children.Add(border);
            }
        }
    }

    private void UpdateHeatmap()
    {
        DateTime _start_time = DateTime.Today.AddDays(-363);
        while (_start_time.DayOfWeek != DayOfWeek.Monday)
        {
            _start_time = _start_time.AddDays(1);
        }
        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j < Columns; j++)
            {
                DateTime currentDate = _start_time.AddDays(j * 7 + i);

                Border border = (Border)HeatmapGrid.Children[i * Columns + j];
                border.ToolTip = new ToolTip
                {
                    Content = $"""
                        更新日期: {currentDate.ToShortDateString()}
                        更新次数: {GetCountByDate(currentDate)}
                        """,
                };
                border.Background = GetColorFromValue(GetCountByDate(currentDate));
            }
        }
    }

    private uint GetCountByDate(DateTime date) => HeatmapData.FirstOrDefault(_item => _item.Date == date)?.Count ?? 0;

    private static SolidColorBrush GetColorFromValue(uint value) => value switch
    {
        0 => new SolidColorBrush(Color.FromRgb(235, 237, 240)),
        <= 40 => new SolidColorBrush(Color.FromRgb(195, 232, 193)),
        <= 80 => new SolidColorBrush(Color.FromRgb(129, 201, 143)),
        <= 120 => new SolidColorBrush(Color.FromRgb(57, 163, 105)),
        >= 120 => new SolidColorBrush(Color.FromRgb(22, 99, 66)),
    };

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private async void Button_Task_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("🖱️ 用户手动触发执行任务");
        if (MainTimer is null)
        {
            AppLogger.Warning("MainTimer 为 null，无法执行");
            return;
        }
        await AutoSearchFunction();
    }

    private async void Button_SendReport_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Info("🖱️ 用户手动触发发送本周执行汇总邮件");
        try
        {
            await Services.EmailNotificationService.SendWeeklySummaryAsync();
            AppLogger.Info("周报邮件发送完成");
            System.Windows.MessageBox.Show("周报邮件已发送，请查收邮箱。", "发送成功",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("手动发送周报邮件异常", ex);
            System.Windows.MessageBox.Show($"发送失败: {ex.Message}", "发送失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
