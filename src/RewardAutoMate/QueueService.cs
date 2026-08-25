using SqlSugar;
using RewardAutoMate.Models;

namespace RewardAutoMate;

/// <summary>
/// SQLite 数据访问层，记录每日搜索次数用于热力图
/// </summary>
public static class QueueService
{
    private static readonly SqlSugarClient Db;

    static QueueService()
    {
        Db = new SqlSugarClient(new ConnectionConfig()
        {
            ConnectionString = "DataSource=HeatMapData.db",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });
        Db.DbMaintenance.CreateDatabase();
        Db.CodeFirst.InitTables(typeof(Models.QueueItem));
    }

    /// <summary>
    /// 从队列中读取前 365 个数据
    /// </summary>
    public static List<Models.QueueItem> DequeueTop365()
    {
        return Db.Queryable<Models.QueueItem>().OrderBy(it => it.Date).Take(365).ToList();
    }

    /// <summary>
    /// 根据 DateTime 修改数据库中的 Count
    /// </summary>
    public static void UpdateItem(Models.QueueItem _item)
    {
        if (Db.Queryable<Models.QueueItem>().InSingle(_item.Date) is null)
        {
            Db.Insertable(_item).ExecuteCommand();
        }
        else
        {
            Db.Updateable<Models.QueueItem>()
               .SetColumns(it => it.Count == _item.Count)
               .Where(it => it.Date == _item.Date)
               .ExecuteCommand();
        }
    }

    /// <summary>
    /// 获取今日计数
    /// </summary>
    public static Models.QueueItem GetTodayCount()
    {
        return Db.Queryable<Models.QueueItem>().InSingle(DateTime.Today)
                ?? new Models.QueueItem() { Date = DateTime.Today, Count = 0 };
    }
}
