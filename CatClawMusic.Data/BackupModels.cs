using System.IO.Compression;
using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using SQLite;

namespace CatClawMusic.Data;

/// <summary>备份数据结构</summary>

public class BackupData
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Playlist> Playlists { get; set; } = new();
    public List<PlaylistSongBackupEntry> PlaylistSongs { get; set; } = new();
    public List<PlayHistoryBackupEntry> PlayHistory { get; set; } = new();
    public List<FavoriteBackupEntry> Favorites { get; set; } = new();
    public List<ArtistBackupEntry> Artists { get; set; } = new();
    public List<ArtistCoverBackupEntry> ArtistCovers { get; set; } = new();
    public List<LlmConfig> LlmConfigs { get; set; } = new();
    public string? CurrentConfigName { get; set; }
    public string? CurrentAgentId { get; set; }
    /// <summary>AI 聊天记录（数据库持久化的消息）</summary>
    public List<ChatMessageRecord> ChatMessages { get; set; } = new();
    /// <summary>AI 记忆文件内容（ai_memory.md 全文）</summary>
    public string? AiMemoryContent { get; set; }
}

public enum BackupItems
{
    None           = 0,
    Playlists      = 1 << 0,  // 歌单 + 歌单歌曲
    PlayHistory    = 1 << 1,  // 播放记录
    Favorites      = 1 << 2,  // 收藏
    Artists        = 1 << 3,  // 艺术家元数据
    LlmConfigs     = 1 << 4,  // AI模型配置
    ArtistCovers   = 1 << 5,  // 艺术家照片
    ChatHistory    = 1 << 6,  // AI聊天记录
    AiMemory       = 1 << 7,  // AI记忆内容
    All            = Playlists | PlayHistory | Favorites | Artists | LlmConfigs | ArtistCovers | ChatHistory | AiMemory,
}

public class BackupProgress
{
    public int Percent { get; set; }
    public string Message { get; set; } = "";
}

public class ArtistBackupEntry
{
    public string Name { get; set; } = "";
    public string? Gender { get; set; }
    public string? Birthday { get; set; }
    public string? Region { get; set; }
    public string? Description { get; set; }
}

public class ArtistCoverBackupEntry
{
    public string ArtistName { get; set; } = "";
    public string FileName { get; set; } = "";
}

public class PlaylistSongBackupEntry
{
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public int Position { get; set; }
}

public class PlayHistoryBackupEntry
{
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public int PlayCount { get; set; }
    public long PlayedAt { get; set; }
}

public class FavoriteBackupEntry
{
    public int SongId { get; set; }
    public string? SongTitle { get; set; }
    public string? SongArtist { get; set; }
    public long AddedAt { get; set; }
}
