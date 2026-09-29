package com.catclaw.music.kt.data.backup

/**
 * 备份文件的数据模型，字段名与 C# `CatClawMusic.Data/BackupModels.cs` 逐一对齐（camelCase 落盘），
 * 因为备份文件是**跨端交换物**：PC 上做的备份要能在安卓上恢复，反之亦然。
 * 所以这里宁可多写几列，也不改名字；读侧对多余/缺失字段一律容忍。
 */
data class BackupData(
    val version: String = "1.0",
    /** C# 那边是 DateTime，序列化成 ISO-8601 本地时间串；安卓侧存 epoch 毫秒再格式化，只为可读 */
    val createdAt: String = "",
    val playlists: List<PlaylistBackupEntry> = emptyList(),
    val playlistSongs: List<PlaylistSongBackupEntry> = emptyList(),
    val playHistory: List<PlayHistoryBackupEntry> = emptyList(),
    val favorites: List<FavoriteBackupEntry> = emptyList(),
    val artists: List<ArtistBackupEntry> = emptyList(),
    val artistCovers: List<ArtistCoverBackupEntry> = emptyList(),
    val llmConfigs: List<com.catclaw.music.kt.core.ai.LlmConfig> = emptyList(),
    val currentConfigName: String? = null,
    /** 安卓侧没有多 Agent 概念，字段保留但恒空，保证 C# 读得懂 */
    val currentAgentId: String? = null,
    val chatMessages: List<ChatMessageBackupEntry> = emptyList(),
    /**
     * Yuki 长期记忆的整份文本。本端存的是 JSON 条目（files/ai_memory.json），
     * MAUI 那边是 ai_memory.md 全文 —— 导入时两种都认，见 ChatMemoryStore.importText。
     */
    val aiMemoryContent: String? = null,
)

/** C# 直接序列化整个 `Playlist` 模型，这里给出安卓表里的同一批列 */
data class PlaylistBackupEntry(
    val id: Int = 0,
    val name: String = "",
    val createdAt: Long = 0,
    val updatedAt: Long = 0,
    val songCount: Int = 0,
    val isSystem: Boolean = false,
    val sortOrder: Int = 0,
)

data class PlaylistSongBackupEntry(
    val playlistId: Int = 0,
    val songId: Int = 0,
    val songTitle: String? = null,
    val songArtist: String? = null,
    val position: Int = 0,
)

data class PlayHistoryBackupEntry(
    val songId: Int = 0,
    val songTitle: String? = null,
    val songArtist: String? = null,
    val playCount: Int = 0,
    val playedAt: Long = 0,
)

data class FavoriteBackupEntry(
    val songId: Int = 0,
    val songTitle: String? = null,
    val songArtist: String? = null,
    val addedAt: Long = 0,
)

data class ArtistBackupEntry(
    val name: String = "",
    val gender: String? = null,
    val birthday: String? = null,
    val region: String? = null,
    val description: String? = null,
)

data class ArtistCoverBackupEntry(
    val artistName: String = "",
    val fileName: String = "",
)

data class ChatMessageBackupEntry(
    val id: Int = 0,
    val role: String = "",
    val content: String = "",
    val timestamp: Long = 0,
)

/** 与 C# `[Flags] BackupItems` 同值，位序不能动：备份文件名与调用方都按它传 */
object BackupItems {
    const val NONE = 0
    const val PLAYLISTS = 1 shl 0
    const val PLAY_HISTORY = 1 shl 1
    const val FAVORITES = 1 shl 2
    const val ARTISTS = 1 shl 3
    const val LLM_CONFIGS = 1 shl 4
    const val ARTIST_COVERS = 1 shl 5
    const val CHAT_HISTORY = 1 shl 6
    const val AI_MEMORY = 1 shl 7
    const val ALL = PLAYLISTS or PLAY_HISTORY or FAVORITES or ARTISTS or LLM_CONFIGS or ARTIST_COVERS or CHAT_HISTORY or AI_MEMORY
}

data class BackupProgress(val percent: Int, val message: String)

/** 备份文件在磁盘上的固定名字：zip 里的 JSON 条目名与旧版裸 JSON 都靠它定位 */
const val BACKUP_JSON_ENTRY = "backup.json"
const val BACKUP_COVERS_PREFIX = "covers/"
