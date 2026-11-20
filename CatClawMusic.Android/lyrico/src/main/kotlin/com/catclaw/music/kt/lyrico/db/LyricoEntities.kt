package com.catclaw.music.kt.lyrico.db

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * Lyrico 侧的歌曲索引。
 *
 * 只存「标签编辑/歌词匹配」需要的信息，用 [filePath] 与 CatClaw 的 `Songs` 关联；
 * 不复制整张曲库（避免两套数据源打架，也让 `catclaw.db` 的 schema 锁不受影响）。
 */
@Entity(
    tableName = "lyrico_song",
    indices = [Index(value = ["filePath"], unique = true)],
)
data class LyricoSongEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    /** 与 catclaw Songs.FilePath 对应的绝对路径或 content:// 串 */
    @ColumnInfo(name = "filePath") val filePath: String,
    val title: String? = null,
    val artist: String? = null,
    val album: String? = null,
    val albumArtist: String? = null,
    val genre: String? = null,
    val date: String? = null,
    val language: String? = null,
    val trackNumber: String? = null,
    val discNumber: Int? = null,
    val durationMs: Long = 0L,
    val bitrate: Int = 0,
    val sampleRate: Int = 0,
    val channels: Int = 0,
    val sizeBytes: Long = 0L,
    /** 文件或文档的最后修改时间（毫秒），用于判断是否需要重读 */
    val lastModified: Long = 0L,
    /** 所属目录；null 表示尚未归类 */
    val folderId: Long? = null,
    /** 歌词来源标记：embedded / sidecar / matched，便于界面显示 */
    val lyricsOrigin: String? = null,
    val lyricsUpdatedAt: Long = 0L,
)

/** 音乐目录（本地路径或 SAF 树）。 */
@Entity(
    tableName = "lyrico_folder",
    indices = [Index(value = ["path"], unique = true)],
)
data class LyricoFolderEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val path: String,
    val displayName: String? = null,
    /** true 表示 path 是 SAF tree uri，写入要拿 ParcelFileDescriptor */
    val isSaf: Boolean = false,
    val enabled: Boolean = true,
    val addedAt: Long = 0L,
)

/** 用户自定义标签键（编辑页可让用户补充非标准字段）。 */
@Entity(
    tableName = "lyrico_custom_tag_key",
    indices = [Index(value = ["name"], unique = true)],
)
data class LyricoCustomTagKeyEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val name: String,
    val enabled: Boolean = true,
)

/** 已安装的 Lyrico 源插件。 */
@Entity(
    tableName = "lyrico_source_plugin",
    indices = [Index(value = ["pluginId"], unique = true)],
)
data class LyricoSourcePluginEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    /** 目录名 / manifest.id */
    val pluginId: String,
    val displayName: String? = null,
    val author: String? = null,
    val versionName: String? = null,
    val versionCode: Int = 0,
    val entry: String = "source.js",
    /** 原始 manifest JSON，便于将来字段扩展而不用改表 */
    val manifestJson: String? = null,
    val enabled: Boolean = true,
    val installedAt: Long = 0L,
    val updatedAt: Long = 0L,
)

/** 在线插件订阅源（对应 Lyrico 的 catalog.json 订阅）。 */
@Entity(
    tableName = "lyrico_plugin_subscription",
    indices = [Index(value = ["url"], unique = true)],
)
data class LyricoPluginSubscriptionEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val url: String,
    val name: String? = null,
    val lastCheckedAt: Long = 0L,
)

/** 诊断日志（Lyrico 有 AppLog 页）。 */
@Entity(tableName = "lyrico_app_log")
data class LyricoAppLogEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val timestamp: Long,
    val level: String,
    val tag: String? = null,
    val message: String,
)
