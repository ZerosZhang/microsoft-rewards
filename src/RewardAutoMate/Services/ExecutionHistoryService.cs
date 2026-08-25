using SqlSugar;
using RewardAutoMate.Models;

namespace RewardAutoMate.Services;

/// <summary>
/// 执行历史数据访问层：持久化每次流程执行的结果，供事后汇总与邮件报告
/// </summary>
public static class ExecutionHistoryService
{
    private static readonly SqlSugarClient Db;

    static ExecutionHistoryService()
    {
        Db = new SqlSugarClient(new ConnectionConfig()
        {
            ConnectionString = "DataSource=RewardsHistory.db",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        Db.DbMaintenance.CreateDatabase();
        Db.CodeFirst.InitTables(typeof(ExecutionRecord), typeof(DailyReportMark));
    }

    /// <summary>
    /// 记录一次执行结果
    /// </summary>
    public static void Add(ExecuteResult _result)
    {
        try
        {
            Db.Insertable(new ExecutionRecord
            {
                StartTime = _result.StartTime,
                EndTime = _result.EndTime == default ? DateTime.Now : _result.EndTime,
                Success = _result.Success,
                PcSearches = _result.PcSearches,
                MobileSearches = _result.MobileSearches,
                TotalSearches = _result.TotalSearches,
                BenefitsClaimed = _result.BenefitsClaimed,
                DailySetCompleted = _result.DailySetCompleted,
                PunchCardsCompleted = _result.PunchCardsCompleted,
                DailyTasksCompleted = _result.DailyTasksCompleted,
                TotalTasks = _result.TotalTasks,
                EstimatedPoints = _result.EstimatedPoints,
                CurrentPoints = _result.CurrentPoints,
                ErrorMessage = _result.ErrorMessage
            }).ExecuteCommand();
        }
        catch (Exception ex)
        {
            Logging.AppLogger.Warning($"保存执行历史失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取指定时间之前最近一条有效的账户积分（CurrentPoints &gt; 0），用于周报积分对比
    /// </summary>
    public static int GetLastPointsBefore(DateTime _before)
    {
        ExecutionRecord? _record = Db.Queryable<ExecutionRecord>()
            .Where(it => it.StartTime < _before && it.CurrentPoints > 0)
            .OrderBy(it => it.StartTime, OrderByType.Desc)
            .First();
        return _record?.CurrentPoints ?? 0;
    }

    /// <summary>
    /// 获取最近 N 天的执行记录（按时间倒序）
    /// </summary>
    public static List<ExecutionRecord> GetRecent(int _days = 7)
    {
        DateTime _from = DateTime.Today.AddDays(-(_days - 1));
        return Db.Queryable<ExecutionRecord>()
                 .Where(it => it.StartTime >= _from)
                 .OrderBy(it => it.StartTime, OrderByType.Desc)
                 .ToList();
    }

    /// <summary>
    /// 获取指定日期区间的执行记录（按时间倒序）
    /// </summary>
    public static List<ExecutionRecord> GetByRange(DateTime _from, DateTime _to)
    {
        return Db.Queryable<ExecutionRecord>()
                 .Where(it => it.StartTime >= _from && it.StartTime < _to)
                 .OrderBy(it => it.StartTime, OrderByType.Desc)
                 .ToList();
    }

    /// <summary>
    /// 本周周报是否已发送（本周一 00:00 之后发送过即为已发）
    /// </summary>
    public static bool IsWeeklyReportSent()
    {
        DateTime _week_start = GetWeekStart(DateTime.Today);
        DailyReportMark? _mark = Db.Queryable<DailyReportMark>().Single();
        return _mark is not null && _mark.LastReportDate >= _week_start;
    }

    /// <summary>
    /// 标记本周周报已发送
    /// </summary>
    public static void MarkWeeklyReportSent()
    {
        DailyReportMark _mark = Db.Queryable<DailyReportMark>().Single() ?? new DailyReportMark();
        _mark.LastReportDate = DateTime.Now;
        if (Db.Queryable<DailyReportMark>().Any())
        {
            Db.Updateable(_mark).ExecuteCommand();
        }
        else
        {
            Db.Insertable(_mark).ExecuteCommand();
        }
    }

    /// <summary>
    /// 获取指定日期所在周的周一 00:00
    /// </summary>
    public static DateTime GetWeekStart(DateTime _date)
    {
        int _days_from_monday = ((int)_date.DayOfWeek + 6) % 7;
        return _date.Date.AddDays(-_days_from_monday);
    }
}

/// <summary>
/// 记录每日日报发送状态（单行表）
/// </summary>
[SugarTable("DailyReportMark")]
public class DailyReportMark
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 最近一次日报发送日期
    /// </summary>
    public DateTime LastReportDate { get; set; }
}
