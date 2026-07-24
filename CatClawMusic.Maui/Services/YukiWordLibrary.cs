using System.Text;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;
using SQLite;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// Yuki 人格词库：基于内置 SQLite 词库数据库（Resources/Raw/wordlib.db，合并了可爱系+傲娇系）。
/// 未配置 LLM 时提供有人格的本地回复；已配置时作为知识库样本注入系统提示词，让模型模仿语气。
/// 词库以数据库形式按需查询，无需把全部词条加载进内存，加载与匹配更快、占用更低。
/// </summary>
public class YukiWordLibrary
{
    private static readonly Lazy<YukiWordLibrary> _lazy = new(() => new YukiWordLibrary());
    public static YukiWordLibrary Instance => _lazy.Value;

    /// <summary>{me} 占位符替换值：Yuki 的自称</summary>
    private const string SelfName = "yuki";
    /// <summary>{name} 占位符替换值：对用户的称呼</summary>
    private const string UserName = "你";
    /// <summary>词库数据库在应用包内的资源名</summary>
    private const string DbAssetName = "wordlib.db";

    private SQLiteAsyncConnection? _db;
    private readonly object _loadLock = new();
    private readonly Random _random = new();

    /// <summary>词库词条（与 wordlib.db 的 WordEntry 表结构一致）</summary>
    private sealed class WordEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string Trigger { get; set; } = "";
        public string Reply { get; set; } = "";
    }

    /// <summary>确保词库数据库已就绪（首次使用时从应用包复制到可写目录并打开）。</summary>
    private SQLiteAsyncConnection? EnsureDb()
    {
        if (_db != null) return _db;
        lock (_loadLock)
        {
            if (_db != null) return _db;
            try
            {
                var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbAssetName);
                if (!File.Exists(dbPath))
                {
                    // APK 内资源是压缩的，SQLite 无法直接打开，先复制到可写目录
                    using var stream = FileSystem.OpenAppPackageFileAsync(DbAssetName).GetAwaiter().GetResult();
                    using var fs = File.Create(dbPath);
                    stream.CopyTo(fs);
                }
                _db = new SQLiteAsyncConnection(dbPath);
            }
            catch (Exception ex)
            {
                Log.Debug("YukiWordLibrary", $"[WordLib] 打开词库数据库失败: {ex.Message}");
            }
            return _db;
        }
    }

    /// <summary>
    /// 根据用户输入获取一条人格回复：优先触发词匹配（取最具体/最长的触发词），无匹配则全库随机兜底。
    /// 词库不可用时返回 null。
    /// </summary>
    public async Task<string?> GetReplyAsync(string userMessage)
    {
        var db = EnsureDb();
        if (db == null) return null;

        var msg = userMessage.Trim();
        WordEntry? chosen = null;
        try
        {
            if (msg.Length > 0)
            {
                // 触发词匹配：用户消息包含触发词，按触发词长度降序取候选（最具体的在前）
                var matches = await db.QueryAsync<WordEntry>(
                    "SELECT * FROM WordEntry WHERE ? LIKE '%' || Trigger || '%' ORDER BY LENGTH(Trigger) DESC LIMIT 50",
                    msg);
                if (matches.Count > 0)
                {
                    var maxLen = matches.Max(m => m.Trigger.Length);
                    var best = matches.Where(m => m.Trigger.Length == maxLen).ToList();
                    chosen = best[_random.Next(best.Count)];
                }
            }

            // 无匹配（或空消息）→ 全库随机兜底
            if (chosen == null)
            {
                var randoms = await db.QueryAsync<WordEntry>(
                    "SELECT * FROM WordEntry ORDER BY RANDOM() LIMIT 1");
                chosen = randoms.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("YukiWordLibrary", $"[WordLib] 查询回复失败: {ex.Message}");
        }

        return chosen != null ? ApplyPlaceholders(chosen.Reply) : null;
    }

    /// <summary>占位符替换：{me}→yuki、{name}→你，并移除 {segment} 及其它残留占位符。</summary>
    private static string ApplyPlaceholders(string template)
    {
        var result = template
            .Replace("{me}", SelfName)
            .Replace("{name}", UserName);
        result = Regex.Replace(result, @"\{[^}]*\}", "");
        return result.Trim();
    }

    /// <summary>
    /// 生成注入 LLM 系统提示词的人格知识样本：随机抽取若干条对话示例，供模型模仿可爱/傲娇语气。
    /// </summary>
    public async Task<string> GetKnowledgePromptAsync(int sampleSize = 30)
    {
        var db = EnsureDb();
        if (db == null) return "";
        try
        {
            var sample = await db.QueryAsync<WordEntry>(
                "SELECT * FROM WordEntry ORDER BY RANDOM() LIMIT ?", sampleSize);
            if (sample.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine("【Yuki 人格对话风格参考——请模仿这种可爱、傲娇、二次元的语气与用户交流】");
            foreach (var e in sample)
            {
                sb.Append("用户：").AppendLine(e.Trigger);
                sb.Append("Yuki：").AppendLine(ApplyPlaceholders(e.Reply));
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Log.Debug("YukiWordLibrary", $"[WordLib] 生成知识样本失败: {ex.Message}");
            return "";
        }
    }
}
