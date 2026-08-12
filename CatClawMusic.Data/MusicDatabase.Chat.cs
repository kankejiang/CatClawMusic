using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using SQLite;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Data;

/// <summary>SQLite 数据库操作层 —— partial 分域文件之一。</summary>
public partial class MusicDatabase
{
    public async Task<int> SaveChatMessageAsync(ChatMessageRecord record)
        => await _database.InsertAsync(record);

    /// <summary>批量插入聊天记录（在单事务中顺序写入）</summary>
    /// <param name="records">待插入的记录集合（Id 应为 0，由数据库自增分配）</param>
    public async Task SaveChatMessagesBatchAsync(IEnumerable<ChatMessageRecord> records)
    {
        var list = records?.ToList();
        if (list == null || list.Count == 0) return;
        await EnsureMaintenanceCompletedAsync();
        await _database.RunInTransactionAsync(tran =>
        {
            foreach (var r in list)
            {
                r.Id = 0; // 由数据库自增分配
                tran.Insert(r);
            }
        });
    }

    /// <summary>获取最近的聊天记录（按时间正序返回，即最旧的在前）</summary>
    /// <param name="limit">返回条数</param>
    /// <param name="beforeId">只加载此 Id 之前的记录（用于向上翻页加载更多），传0或null表示不限制</param>
    public async Task<List<ChatMessageRecord>> GetRecentChatMessagesAsync(int limit, int? beforeId = null)
    {
        List<ChatMessageRecord> list;
        if (beforeId.HasValue && beforeId.Value > 0)
        {
            // 向上翻页：加载指定Id之前的记录
            list = await _database.Table<ChatMessageRecord>()
                .Where(r => r.Id < beforeId.Value)
                .OrderByDescending(r => r.Id)
                .Take(limit)
                .ToListAsync();
        }
        else
        {
            // 首次加载：取最近N条
            list = await _database.Table<ChatMessageRecord>()
                .OrderByDescending(r => r.Id)
                .Take(limit)
                .ToListAsync();
        }
        // 原为降序取最近N条，反转为时间正序返回（最旧的在前）。
        // 直接原地反转，避免 .Result 阻塞与 AsEnumerable().Reverse().ToList() 的中间集合分配。
        list.Reverse();
        return list;
    }

    /// <summary>获取聊天记录总数</summary>
    public async Task<int> GetChatMessageCountAsync()
        => await _database.Table<ChatMessageRecord>().CountAsync();

    /// <summary>裁剪聊天记录，只保留最近指定条数</summary>
    public async Task TrimChatMessagesAsync(int keepCount)
    {
        try
        {
            var count = await _database.Table<ChatMessageRecord>().CountAsync();
            if (count <= keepCount) return;

            var toDelete = count - keepCount;
            await _database.ExecuteAsync(
                "DELETE FROM ChatMessageRecord WHERE Id IN (SELECT Id FROM ChatMessageRecord ORDER BY Id ASC LIMIT ?)",
                toDelete);
        }
        catch { }
    }

    /// <summary>清空所有聊天记录</summary>
    public async Task ClearChatMessagesAsync()
        => await _database.DeleteAllAsync<ChatMessageRecord>();
}
