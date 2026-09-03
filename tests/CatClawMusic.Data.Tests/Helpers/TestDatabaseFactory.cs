using CatClawMusic.Data;
using SQLite;

namespace CatClawMusic.Data.Tests.Helpers;

/// <summary>为每个测试提供独立的临时 SQLite 数据库文件</summary>
public static class TestDatabaseFactory
{
    public static string CreateDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "catclaw_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test.db3");
    }

    /// <summary>创建已初始化的 MusicDatabase(表、索引、迁移全部就绪)</summary>
    public static async Task<MusicDatabase> CreateInitializedAsync()
    {
        var db = new MusicDatabase(CreateDbPath());
        await db.EnsureInitializedAsync();
        return db;
    }

    private sealed class IndexRow
    {
        public string Name { get; set; } = "";
        public string Sql { get; set; } = "";
    }

    /// <summary>直接查询 sqlite_master,用于断言索引/表结构</summary>
    public static async Task<List<(string Name, string Sql)>> GetIndexesAsync(string dbPath, string tableName)
    {
        var conn = new SQLiteAsyncConnection(dbPath);
        try
        {
            var rows = await conn.QueryAsync<IndexRow>(
                "SELECT name, COALESCE(sql,'') AS Sql FROM sqlite_master WHERE type='index' AND tbl_name=? ORDER BY name",
                tableName);
            return rows.Select(r => (r.Name, r.Sql)).ToList();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>直接执行原生 SQL(用于构造"旧版本遗留数据"场景)</summary>
    public static void ExecuteRaw(string dbPath, string sql, params object[] args)
    {
        using var raw = new SQLiteConnection(dbPath);
        raw.Execute(sql, args);
    }
}
