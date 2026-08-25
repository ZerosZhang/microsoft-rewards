using SqlSugar;

namespace RewardAutoMate.Models;

/// <summary>
/// 队列数据实体类，用于热力图展示
/// </summary>
public class QueueItem
{
    [SugarColumn(IsPrimaryKey = true)]
    public DateTime Date { get; set; }
    public uint Count { get; set; }
}
