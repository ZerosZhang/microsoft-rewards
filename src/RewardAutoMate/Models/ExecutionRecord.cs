using SqlSugar;

namespace RewardAutoMate.Models;

/// <summary>
/// 一次完整 Rewards 流程的执行记录（用于事后查看历史执行情况）
/// </summary>
[SugarTable("ExecutionRecord")]
public class ExecutionRecord
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 执行开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 执行结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 是否整体成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// PC 端搜索次数
    /// </summary>
    public int PcSearches { get; set; }

    /// <summary>
    /// 移动端搜索次数
    /// </summary>
    public int MobileSearches { get; set; }

    /// <summary>
    /// 总搜索次数
    /// </summary>
    public int TotalSearches { get; set; }

    /// <summary>
    /// 福利领取数量
    /// </summary>
    public int BenefitsClaimed { get; set; }

    /// <summary>
    /// Daily Set 完成数量
    /// </summary>
    public int DailySetCompleted { get; set; }

    /// <summary>
    /// Punch Cards 完成数量
    /// </summary>
    public int PunchCardsCompleted { get; set; }

    /// <summary>
    /// 日常任务完成数量
    /// </summary>
    public int DailyTasksCompleted { get; set; }

    /// <summary>
    /// 任务总数
    /// </summary>
    public int TotalTasks { get; set; }

    /// <summary>
    /// 估计获得积分（程序对任务加分的粗估，仅供参考）
    /// </summary>
    public int EstimatedPoints { get; set; }

    /// <summary>
    /// 执行时读取到的账户当前总积分（0 表示未读取到）
    /// </summary>
    public int CurrentPoints { get; set; }

    /// <summary>
    /// 错误信息（成功时为空）
    /// </summary>
    [SugarColumn(ColumnDataType = "varchar(2000)")]
    public string ErrorMessage { get; set; } = "";

    /// <summary>
    /// 本次执行耗时（分钟）
    /// </summary>
    public double DurationMinutes => (EndTime - StartTime).TotalMinutes;
}
