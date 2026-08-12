using System.IO.Compression;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>
/// 备份恢复服务主体：主流程 + 静态文件工具；数据加载 / 恢复 / ZIP 打包
/// 见同目录 BackupService.DataLoad / DataRestore / Zip 的 partial 文件。
/// </summary>
public partial class BackupService
{
    /// <summary>数据库访问实例</summary>
    private readonly MusicDatabase _db;
    /// <summary>AI Agent 配置存储（保留兼容，当前直接通过 AgentService 静态方法访问）</summary>
    private readonly IAgentConfigStorage _configStorage;
    /// <summary>艺术家封面缓存目录绝对路径</summary>
    private readonly string _artistCoversDir;
    /// <summary>AI 记忆文件路径（ai_memory.md）</summary>
    private readonly string _aiMemoryFilePath;

    /// <summary>JSON 序列化选项：缩进输出 + camelCase 命名</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化备份与恢复服务。
    /// </summary>
    /// <param name="db">数据库访问实例。</param>
    /// <param name="configStorage">AI Agent 配置存储。</param>
    /// <param name="artistCoversDir">艺术家封面缓存目录路径。</param>
    /// <param name="aiMemoryFilePath">AI 记忆文件路径。</param>
    public BackupService(MusicDatabase db, IAgentConfigStorage configStorage, string artistCoversDir, string aiMemoryFilePath)
    {
        _db = db;
        _configStorage = configStorage;
        _artistCoversDir = artistCoversDir;
        _aiMemoryFilePath = aiMemoryFilePath;
    }

    /// <summary>执行备份，将数据写入指定目录下的 CatClawMusic 文件夹</summary>
    /// <param name="externalStoragePath">外部存储根目录（如 /storage/emulated/0）</param>
    /// <param name="items">要备份的数据类别</param>
    /// <param name="progress">进度回调</param>
    /// <returns>备份文件路径</returns>
    public async Task<string> BackupAsync(string externalStoragePath, BackupItems items = BackupItems.All, IProgress<BackupProgress>? progress = null)
    {
        await _db.EnsureInitializedAsync();
        Report(progress, 0, "准备备份...");

        var data = new BackupData();

        if (items.HasFlag(BackupItems.Playlists))
        {
            data.Playlists = await _db.GetAllPlaylistsAsync();
            data.PlaylistSongs = await LoadAllPlaylistSongsWithInfoAsync();
        }
        if (items.HasFlag(BackupItems.PlayHistory))
            data.PlayHistory = await LoadPlayHistoryWithInfoAsync();
        if (items.HasFlag(BackupItems.Favorites))
            data.Favorites = await LoadFavoritesWithInfoAsync();
        Report(progress, 15, "正在读取基础数据...");

        var dir = System.IO.Path.Combine(externalStoragePath, "CatClawMusic");
        System.IO.Directory.CreateDirectory(dir);

        var baseName = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        var jsonPath = System.IO.Path.Combine(dir, $"{baseName}.json");
        var zipPath = System.IO.Path.Combine(dir, $"{baseName}.zip");

        if (items.HasFlag(BackupItems.Artists))
        {
            data.Artists = await LoadArtistMetadataAsync();
            Report(progress, 25, "正在读取艺术家元数据...");
        }
        if (items.HasFlag(BackupItems.ArtistCovers))
        {
            data.ArtistCovers = await LoadArtistCoversAsync(jsonPath, data, progress, 30, 60);
            Report(progress, 60, "正在整理备份数据...");
        }
        else
        {
            Report(progress, 50, "正在整理备份数据...");
        }
        if (items.HasFlag(BackupItems.LlmConfigs))
        {
            data.LlmConfigs = AgentService.LoadAllConfigs();
            data.CurrentConfigName = AgentService.GetCurrentConfigName();
            data.CurrentAgentId = AgentService.LoadCurrentAgentId();
        }
        if (items.HasFlag(BackupItems.ChatHistory))
        {
            var chatCount = await _db.GetChatMessageCountAsync();
            data.ChatMessages = await _db.GetRecentChatMessagesAsync(chatCount);
            Report(progress, 65, $"已读取 {data.ChatMessages.Count} 条聊天记录");
        }
        if (items.HasFlag(BackupItems.AiMemory))
        {
            if (System.IO.File.Exists(_aiMemoryFilePath))
                data.AiMemoryContent = await System.IO.File.ReadAllTextAsync(_aiMemoryFilePath);
            Report(progress, 70, "已读取 AI 记忆");
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await System.IO.File.WriteAllTextAsync(jsonPath, json);
        Report(progress, 75, "正在生成备份文件...");

        // 打包为单一 zip 文件（JSON + covers），完成后删除中间文件
        CreateBackupZip(zipPath, jsonPath);
        CleanupBackupTempFiles(jsonPath);
        Report(progress, 100, "备份完成");

        return zipPath;
    }

    /// <summary>从备份文件恢复数据</summary>
    /// <param name="filePath">备份文件路径</param>
    /// <param name="items">要恢复的数据类别</param>
    /// <param name="progress">进度回调</param>
    public async Task RestoreAsync(string filePath, BackupItems items = BackupItems.All, IProgress<BackupProgress>? progress = null)
    {
        await _db.EnsureInitializedAsync();
        Report(progress, 0, "准备恢复...");

        var isZip = System.IO.Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        var data = isZip
            ? await ReadBackupDataFromZipAsync(filePath)
            : JsonSerializer.Deserialize<BackupData>(await System.IO.File.ReadAllTextAsync(filePath), JsonOptions)
                ?? throw new InvalidOperationException("备份文件格式无效");
        Report(progress, 15, "正在读取备份信息...");

        if (items.HasFlag(BackupItems.Playlists))
        {
            await RestorePlaylistsAsync(data);
            Report(progress, 35, "正在恢复歌单...");
        }

        if (items.HasFlag(BackupItems.PlayHistory))
        {
            await RestorePlayHistoryAsync(data);
            Report(progress, 50, "正在恢复播放记录...");
        }

        if (items.HasFlag(BackupItems.Favorites))
        {
            await RestoreFavoritesAsync(data);
            Report(progress, 60, "正在恢复收藏...");
        }

        if (items.HasFlag(BackupItems.Artists))
        {
            await RestoreArtistMetadataAsync(data);
            Report(progress, 70, "正在恢复艺术家元数据...");
        }

        if (items.HasFlag(BackupItems.ArtistCovers))
        {
            if (isZip)
                await RestoreArtistCoversFromZipAsync(filePath, data, progress, 75, 95);
            else
                await RestoreArtistCoversAsync(filePath, data, progress, 75, 95);
            Report(progress, 95, "正在完成恢复...");
        }
        else if (items.HasFlag(BackupItems.Artists))
        {
            Report(progress, 85, "正在完成恢复...");
        }

        if (items.HasFlag(BackupItems.LlmConfigs))
            RestoreLlmConfigs(data);

        if (items.HasFlag(BackupItems.ChatHistory))
        {
            await RestoreChatHistoryAsync(data);
            Report(progress, 90, "已恢复聊天记录");
        }

        if (items.HasFlag(BackupItems.AiMemory))
        {
            await RestoreAiMemoryAsync(data);
            Report(progress, 95, "已恢复 AI 记忆");
        }

        Report(progress, 100, "恢复完成");
    }

    /// <summary>获取备份目录路径</summary>
    public static string GetBackupDirectory(string externalStoragePath)
        => System.IO.Path.Combine(externalStoragePath, "CatClawMusic");

    /// <summary>列出备份目录中所有备份文件（支持 .zip 和旧版 .json）</summary>
    public static List<string> ListBackups(string externalStoragePath)
    {
        var dir = GetBackupDirectory(externalStoragePath);
        if (!System.IO.Directory.Exists(dir)) return new List<string>();
        return System.IO.Directory.GetFiles(dir)
            .Where(f =>
            {
                var name = System.IO.Path.GetFileName(f);
                return name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) &&
                       (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>从备份文件读取简要信息（支持 .zip 和旧版 .json）</summary>
    public static async Task<BackupData?> ReadBackupInfoAsync(string filePath)
    {
        try
        {
            var isZip = System.IO.Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            if (isZip)
                return await ReadBackupDataFromZipAsync(filePath);

            var json = await System.IO.File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<BackupData>(json, JsonOptions);
        }
        catch { return null; }
    }
}
    /// <param name="jsonPath">backup.json 文件路径。</param>


