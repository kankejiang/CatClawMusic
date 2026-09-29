package com.catclaw.music.kt.data.backup

import android.content.Context
import com.catclaw.music.kt.core.ai.LlmConfig
import com.catclaw.music.kt.data.ai.ChatMemoryStore
import com.catclaw.music.kt.data.ai.LlmConfigStore
import com.catclaw.music.kt.data.db.BackupDao
import com.catclaw.music.kt.data.db.ChatDao
import com.catclaw.music.kt.data.db.entity.ChatMessageEntity
import dagger.hilt.android.qualifiers.ApplicationContext
import java.io.File
import javax.inject.Inject
import javax.inject.Singleton

/**
 * [BackupStore] 的 Room 实现。DAO 只负责取数与写回，恢复语义全在 [BackupService]，
 * 这样语义可以用假 store 测（见 `BackupServiceTest`），不必起内存数据库。
 */
@Singleton
class RoomBackupStore @Inject constructor(
    private val backup: BackupDao,
    private val chat: ChatDao,
    private val configs: LlmConfigStore,
    private val memory: ChatMemoryStore,
    @ApplicationContext private val context: Context,
) : BackupStore {

    /**
     * 记忆走 [ChatMemoryStore] 而不是直接读写那个文件：它内存里有一份副本，
     * 绕过它改盘会让"设置页看到的"和"注入给模型的"两份数据从此不一致。
     */
    override suspend fun aiMemoryText(): String? = memory.exportText()

    override suspend fun restoreAiMemory(text: String): Int = memory.importText(text)

    override suspend fun playlists() = backup.backupPlaylists().map {
        PlaylistBackupEntry(it.id, it.name, it.createdAt, it.updatedAt, it.songCount, it.isSystem, it.sortOrder)
    }

    override suspend fun playlistSongs() = backup.backupPlaylistSongs().map {
        PlaylistSongBackupEntry(it.playlistId, it.songId, it.songTitle, it.songArtist, it.position)
    }

    override suspend fun playHistory() = backup.backupPlayHistory().map {
        PlayHistoryBackupEntry(it.songId, it.songTitle, it.songArtist, it.playCount, it.playedAt)
    }

    override suspend fun favorites() = backup.backupFavorites().map {
        FavoriteBackupEntry(it.songId, it.songTitle, it.songArtist, it.addedAt)
    }

    override suspend fun artists() = backup.backupArtists().map {
        ArtistBackupEntry(it.name, it.gender, it.birthday, it.region, it.description)
    }

    /**
     * 本端封面缓存在 `filesDir/artist_covers`。文件名规则与取词服务一致：`艺术家名.jpg`，
     * 来源后缀（`_qq` / `_netease`）也算，所以按「同名或同名前缀」匹配第一个真实存在的文件。
     * 目录不存在时返回空表 —— 备份里的 covers 段自然为空，不会凭空造条目。
     */
    override suspend fun artistCoverFiles(): List<Pair<String, String>> {
        val dir = coverCacheDir()
        val files = dir.listFiles()?.toList().orEmpty()
        if (files.isEmpty()) return emptyList()
        return backup.artistNames().mapNotNull { name ->
            val safe = BackupService.sanitize(name)
            val hit = files.firstOrNull {
                it.isFile && (it.nameWithoutExtension.equals(safe, ignoreCase = true) ||
                    it.nameWithoutExtension.startsWith("${safe}_"))
            }
            hit?.let { name to it.absolutePath }
        }
    }

    override suspend fun setArtistCover(artistName: String, path: String) = backup.setArtistCover(artistName, path)

    override suspend fun artistNames() = backup.artistNames()

    override suspend fun llmConfigs(): List<LlmConfig> = configs.configs.value

    override suspend fun currentConfigName() = configs.currentName.value

    override suspend fun chatMessages() = chat.recent(chat.count().coerceAtLeast(1)).map {
        ChatMessageBackupEntry(it.id, it.role, it.content, it.timestamp)
    }

    override suspend fun localSongs() = backup.songRefs().map { BackupStore.SongRef(it.id, it.title, it.artist) }

    override suspend fun createPlaylist(name: String) = backup.insertPlaylist(name, System.currentTimeMillis()).toInt()

    /**
     * 恢复进已有歌单时：先读现有条目，位置从末尾续起，别把用户排好的顺序打乱。
     * 重复本身由库上的 `ux_playlist_songs_playlist_song` + INSERT OR IGNORE 挡掉（`BackupDaoSqlTest` 钉住）。
     */
    override suspend fun addPlaylistSongs(playlistId: Int, songIds: List<Int>) {
        val existing = backup.existingPlaylistSongIds(playlistId).toHashSet()
        var position = existing.size
        songIds.forEach { songId ->
            if (songId in existing) return@forEach
            backup.insertPlaylistSong(playlistId, songId, position++)
            existing += songId
        }
    }

    /** 一条备份条目落成一条历史行，等价于 C# 那边按次数重复 RecordPlay */
    override suspend fun addPlayCounts(counts: Map<Int, Int>) {
        val now = System.currentTimeMillis()
        counts.forEach { (songId, count) -> backup.insertHistory(songId, now, count) }
    }

    override suspend fun addFavorites(songIds: Collection<Int>) {
        val now = System.currentTimeMillis()
        songIds.forEach { backup.insertFavorite(it, now) }
    }

    override suspend fun fillEmptyArtistFields(entries: List<ArtistBackupEntry>) {
        entries.forEach { backup.fillArtistFields(it.name, it.gender, it.birthday, it.region, it.description) }
    }

    override suspend fun restoreLlmConfigs(configs: List<LlmConfig>, currentName: String?) {
        this.configs.saveAll(configs)
        currentName?.takeIf { it.isNotBlank() }?.let(this.configs::setCurrentName)
    }

    override suspend fun replaceChatMessages(messages: List<ChatMessageBackupEntry>) {
        backup.clearChat()
        messages.forEach { backup.insertChat(ChatMessageEntity(role = it.role, content = it.content, timestamp = it.timestamp)) }
    }

    private fun coverCacheDir() = File(context.filesDir, "artist_covers")
}
