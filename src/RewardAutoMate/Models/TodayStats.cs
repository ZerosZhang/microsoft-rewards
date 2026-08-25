namespace RewardAutoMate.Models;

/// <summary>
/// 今日 Rewards 执行统计
/// </summary>
public class TodayStats
{
    /// <summary>
    /// 当前账户总积分
    /// </summary>
    public int CurrentPoints { get; set; }

    /// <summary>
    /// 今日程序启动时的积分（用于计算今日实际获得）
    /// </summary>
    public int TodayStartPoints { get; set; }

    /// <summary>
    /// 今日已获得积分（实际值 = 当前积分 - 今日起始积分）
    /// </summary>
    public int TodayEarnedPoints => TodayStartPoints > 0 && CurrentPoints >= TodayStartPoints
        ? CurrentPoints - TodayStartPoints
        : 0;

    /// <summary>
    /// 可领取数量（Dashboard 右侧卡片）
    /// </summary>
    public int ClaimableCount { get; set; }

    /// <summary>
    /// PC 端搜索完成次数（本程序实际执行）
    /// </summary>
    public int PcSearchesDone { get; set; }

    /// <summary>
    /// PC 端搜索目标次数
    /// </summary>
    public int PcSearchesTarget { get; set; }

    /// <summary>
    /// 移动端搜索完成次数（本程序实际执行）
    /// </summary>
    public int MobileSearchesDone { get; set; }

    /// <summary>
    /// 移动端搜索目标次数
    /// </summary>
    public int MobileSearchesTarget { get; set; }

    /// <summary>
    /// Daily Set 已完成数量（从 Dashboard 读取）
    /// </summary>
    public int DailySetDone { get; set; }

    /// <summary>
    /// Daily Set 总数量
    /// </summary>
    public int DailySetTotal { get; set; }

    /// <summary>
    /// Dashboard 搜索任务进度（如 必应搜索: 1/1）
    /// </summary>
    public int DashboardSearchDone { get; set; }
    public int DashboardSearchTotal { get; set; }

    /// <summary>
    /// Dashboard 活动任务进度（如 活动: 0/3）
    /// </summary>
    public int DashboardActivityDone { get; set; }
    public int DashboardActivityTotal { get; set; }

    /// <summary>
    /// Dashboard 签到进度（如 签到: 0/1）
    /// </summary>
    public int DashboardCheckInDone { get; set; }
    public int DashboardCheckInTotal { get; set; }

    /// <summary>
    /// 福利已领取数量（本程序实际领取）
    /// </summary>
    public int BenefitsClaimed { get; set; }

    /// <summary>
    /// Punch Cards 已完成数量（本程序实际完成）
    /// </summary>
    public int PunchCardsDone { get; set; }

    /// <summary>
    /// 日常任务已完成数量（本程序实际完成）
    /// </summary>
    public int DailyTasksDone { get; set; }

    /// <summary>
    /// 会员等级
    /// </summary>
    public string Level { get; set; } = "";

    /// <summary>
    /// 下次自动执行时间
    /// </summary>
    public DateTime? NextExecuteTime { get; set; }

    /// <summary>
    /// 上次更新时间
    /// </summary>
    public DateTime LastUpdateTime { get; set; }

    /// <summary>
    /// PC 搜索进度百分比
    /// </summary>
    public int PcSearchPercent => PcSearchesTarget > 0 ? (int)((double)PcSearchesDone / PcSearchesTarget * 100) : 0;

    /// <summary>
    /// Mobile 搜索进度百分比
    /// </summary>
    public int MobileSearchPercent => MobileSearchesTarget > 0 ? (int)((double)MobileSearchesDone / MobileSearchesTarget * 100) : 0;
}
