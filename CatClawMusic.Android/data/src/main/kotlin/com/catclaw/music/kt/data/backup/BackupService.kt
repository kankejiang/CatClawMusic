package com.catclaw.music.kt.data.backup

import com.catclaw.music.kt.core.ai.LlmConfig
import com.catclaw.music.kt.data.ai.LlmConfigStore
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream

/** 备份/恢复要读写的所有数据出口。Room 实现见 [RoomBackupStore]；单测用假实现盯语义。 */
interface BackupStore {
    /** 本地歌曲索引，恢复时靠它把备份里的歌对回本地 id */
    data class SongRef(val id: Int, val title: String, val artist: String)

    suspend fun playlists(): List<PlaylistBackupEntry>
    suspend fun playlistSongs(): List<PlaylistSongBackupEntry>
    suspend fun playHistory(): List<PlayHistoryBackupEntry>
    suspend fun favorites(): List<FavoriteBackupEntry>
    suspend fun artists(): List<ArtistBackupEntry>
    /** (艺术家名, 封面绝对路径)，只含文件此刻真在盘上的那些 */
    suspend fun artistCoverFiles(): List<Pair<String, String>>
    suspend fun setArtistCover(artistName: String, path: String)
    suspend fun artistNames(): List<String>
    suspend fun llmConfigs(): List<LlmConfig>
    suspend fun currentConfigName(): String?
    suspend fun chatMessages(): List<ChatMessageBackupEntry>
    suspend fun localSongs(): List<SongRef>

    suspend fun createPlaylist(name: String): Int
    suspend fun addPlaylistSongs(playlistId: Int, songIds: List<Int>)
    suspend fun addPlayCounts(counts: Map<Int, Int>)
    suspend fun addFavorites(songIds: Collection<Int>)
    /** 只补空字段，别覆盖用户自己改过的资料（C# `RestoreArtistMetadataAsync` 同规则） */
    suspend fun fillEmptyArtistFields(entries: List<ArtistBackupEntry>)
    suspend fun restoreLlmConfigs(configs: List<LlmConfig>, currentName: String?)
    suspend fun replaceChatMessages(messages: List<ChatMessageBackupEntry>)
    /** Yuki 长期记忆的整份文本（本端是 JSON 条目；MAUI 那边是 ai_memory.md 全文） */
    suspend fun aiMemoryText(): String?

    /** 恢复记忆。返回条数，0 表示这份备份里没有记忆 */
    suspend fun restoreAiMemory(text: String): Int
}

/**
 * 备份 = 一份 JSON（+ 艺术家照片）打成单个 `backup_yyyyMMdd_HHmmss.zip`，落在 `<root>/CatClawMusic/`。
 * 流程、进度百分比、恢复语义逐条对齐 C# `BackupService`，因为备份文件是跨端交换物。
 *
 * 安卓侧没有的两样在这里留形不留实：`currentAgentId`（本端无多 Agent，字段照写空值以便 C# 读得懂）。
 * `aiMemoryContent` 以前也是留形不留实，现在本端有了 ai_memory.json，它就真的随备份走（BackupItems.AI_MEMORY）。
 */
class BackupService(
    private val store: BackupStore,
    private val coversDir: File,
    private val now: () -> Long = System::currentTimeMillis,
) {

    suspend fun backup(root: File, items: Int = BackupItems.ALL, onProgress: (BackupProgress) -> Unit = {}): File {
        report(onProgress, 0, "准备备份...")
        var data = BackupData(createdAt = stampFormat.format(Date(now())))

        if (items.has(BackupItems.PLAYLISTS)) {
            data = data.copy(playlists = store.playlists(), playlistSongs = store.playlistSongs())
        }
        if (items.has(BackupItems.PLAY_HISTORY)) data = data.copy(playHistory = store.playHistory())
        if (items.has(BackupItems.FAVORITES)) data = data.copy(favorites = store.favorites())
        report(onProgress, 15, "正在读取基础数据...")

        val dir = directory(root)
        dir.mkdirs()
        val base = "backup_" + fileStamp.format(Date(now()))
        val zipFile = File(dir, "$base.zip")

        if (items.has(BackupItems.ARTISTS)) {
            data = data.copy(artists = store.artists())
            report(onProgress, 25, "正在读取艺术家元数据...")
        }

        // 封面先落到与 zip 同名的临时目录，再一起打包；C# 用同一个中间目录，这里保持一致
        val covers = if (items.has(BackupItems.ARTIST_COVERS)) {
            stageCovers(File(dir, "${base}_covers")).also { covered ->
                data = data.copy(artistCovers = covered.map { it.first })
                report(onProgress, 60, "正在整理备份数据...")
            }
        } else {
            report(onProgress, 50, "正在整理备份数据...")
            emptyList()
        }

        if (items.has(BackupItems.LLM_CONFIGS)) {
            data = data.copy(llmConfigs = store.llmConfigs(), currentConfigName = store.currentConfigName())
        }
        if (items.has(BackupItems.CHAT_HISTORY)) {
            val messages = store.chatMessages()
            data = data.copy(chatMessages = messages)
            report(onProgress, 65, "已读取 ${messages.size} 条聊天记录")
        }
        if (items.has(BackupItems.AI_MEMORY)) {
            // 空记忆也要写出去（"[]"），这样"用户把所有记忆删光了"和"这端根本没有记忆"
            // 在备份文件里是两件事 —— 前者恢复后应该清空，后者不该动对面的数据
            data = data.copy(aiMemoryContent = store.aiMemoryText())
        }

        val json = BackupJson.encode(data, LlmConfigStore::encodeConfigJson)
        zipFile.outputStream().use { out ->
            ZipOutputStream(out.buffered()).use { zip ->
                zip.putNextEntry(ZipEntry(BACKUP_JSON_ENTRY))
                zip.write(json.toByteArray(Charsets.UTF_8))
                zip.closeEntry()
                covers.forEach { (entry, file) ->
                    zip.putNextEntry(ZipEntry("$BACKUP_COVERS_PREFIX${entry.fileName}"))
                    file.inputStream().use { it.copyTo(zip) }
                    zip.closeEntry()
                }
            }
        }
        report(onProgress, 75, "正在生成备份文件...")
        File(dir, "${base}_covers").deleteRecursively()
        report(onProgress, 100, "备份完成")
        return zipFile
    }

    suspend fun restore(file: File, items: Int = BackupItems.ALL, onProgress: (BackupProgress) -> Unit = {}) {
        report(onProgress, 0, "准备恢复...")
        val data = readData(file)
        report(onProgress, 15, "正在读取备份信息...")

        val songs = store.localSongs()
        val byId = songs.map { it.id }.toSet()
        val keyToId = LinkedHashMap<String, Int>()
        songs.forEach { val k = songKey(it.title, it.artist); if (k !in keyToId) keyToId[k] = it.id }
        fun match(songId: Int, title: String?, artist: String?): Int? =
            if (songId in byId) songId
            else if (!title.isNullOrEmpty()) keyToId[songKey(title, artist.orEmpty())]
            else null

        if (items.has(BackupItems.PLAYLISTS)) {
            val existing = store.playlists().map { it.name }.toHashSet()
            val oldToNew = HashMap<Int, Int>()
            data.playlists.forEach { pl ->
                if (pl.name in existing) return@forEach
                oldToNew[pl.id] = store.createPlaylist(pl.name)
            }
            val grouped = LinkedHashMap<Int, MutableList<Int>>()
            data.playlistSongs.forEach { ps ->
                val newPlaylist = oldToNew[ps.playlistId] ?: return@forEach
                val songId = match(ps.songId, ps.songTitle, ps.songArtist) ?: return@forEach
                grouped.getOrPut(newPlaylist) { mutableListOf() }.add(songId)
            }
            grouped.forEach { (playlistId, songIds) -> store.addPlaylistSongs(playlistId, songIds) }
            report(onProgress, 35, "正在恢复歌单...")
        }

        if (items.has(BackupItems.PLAY_HISTORY)) {
            val merged = LinkedHashMap<Int, Int>()
            data.playHistory.forEach { ph ->
                val songId = match(ph.songId, ph.songTitle, ph.songArtist) ?: return@forEach
                if (ph.playCount > 0) merged[songId] = (merged[songId] ?: 0) + ph.playCount
            }
            if (merged.isNotEmpty()) store.addPlayCounts(merged)
            report(onProgress, 50, "正在恢复播放记录...")
        }

        if (items.has(BackupItems.FAVORITES)) {
            val toAdd = LinkedHashSet<Int>()
            data.favorites.forEach { fav ->
                match(fav.songId, fav.songTitle, fav.songArtist)?.let { toAdd.add(it) }
            }
            if (toAdd.isNotEmpty()) store.addFavorites(toAdd)
            report(onProgress, 60, "正在恢复收藏...")
        }

        if (items.has(BackupItems.ARTISTS)) {
            store.fillEmptyArtistFields(data.artists)
            report(onProgress, 70, "正在恢复艺术家元数据...")
        }

        if (items.has(BackupItems.ARTIST_COVERS) && data.artistCovers.isNotEmpty()) {
            restoreCovers(file, data, onProgress, 75, 95)
            report(onProgress, 95, "正在完成恢复...")
        } else if (items.has(BackupItems.ARTISTS)) {
            report(onProgress, 85, "正在完成恢复...")
        }

        if (items.has(BackupItems.LLM_CONFIGS) && data.llmConfigs.isNotEmpty()) {
            store.restoreLlmConfigs(data.llmConfigs, data.currentConfigName)
        }
        if (items.has(BackupItems.CHAT_HISTORY) && data.chatMessages.isNotEmpty()) {
            store.replaceChatMessages(data.chatMessages)
            report(onProgress, 90, "已恢复聊天记录")
        }
        // 备份里没有这段就完全不动本端记忆：C# 的 RestoreAiMemoryAsync 也是"空内容直接 return"，
        // 否则拿一份旧备份恢复会把用户后来攒下的记忆抹平
        if (items.has(BackupItems.AI_MEMORY) && !data.aiMemoryContent.isNullOrBlank()) {
            val n = store.restoreAiMemory(data.aiMemoryContent)
            report(onProgress, 95, "已恢复 $n 条长期记忆")
        }
        report(onProgress, 100, "恢复完成")
    }

    private suspend fun restoreCovers(file: File, data: BackupData, onProgress: (BackupProgress) -> Unit, from: Int, to: Int) {
        if (!file.name.endsWith(".zip")) return
        coversDir.mkdirs()
        val known = store.artistNames().associateBy { it.lowercase(Locale.ROOT) }
        ZipFile(file).use { zip ->
            val total = data.artistCovers.size
            data.artistCovers.forEachIndexed { index, entry ->
                val artistName = known[entry.artistName.lowercase(Locale.ROOT)]
                val source = zip.getEntry("$BACKUP_COVERS_PREFIX${entry.fileName}")
                if (artistName != null && source != null) {
                    runCatching {
                        // 单张失败不影响整体：C# 那边也是 catch 掉继续走
                        val dest = File(coversDir, "${sanitize(artistName)}.jpg")
                        zip.getInputStream(source).use { input -> dest.outputStream().use { input.copyTo(it) } }
                        store.setArtistCover(artistName, dest.absolutePath)
                    }
                }
                report(onProgress, from + (to - from) * (index + 1) / total.coerceAtLeast(1), "正在解压艺术家照片 (${index + 1}/$total)...")
            }
        }
    }

    /** 把盘上的封面复制进临时目录并改名成安全文件名；同名冲突追加序号（与 C# 同规则） */
    private suspend fun stageCovers(tempDir: File): List<Pair<ArtistCoverBackupEntry, File>> {
        val taken = HashSet<String>()
        return store.artistCoverFiles().mapNotNull { (artistName, path) ->
            val source = File(path)
            if (!source.isFile) return@mapNotNull null
            val safe = sanitize(artistName)
            var fileName = "$safe.jpg"
            var index = 1
            while (fileName in taken) {
                fileName = "${safe}_$index.jpg"
                index++
            }
            taken += fileName
            val dest = File(tempDir.apply { mkdirs() }, fileName)
            runCatching { source.copyTo(dest, overwrite = true) }.getOrNull() ?: return@mapNotNull null
            ArtistCoverBackupEntry(artistName, fileName) to dest
        }
    }

    private fun readData(file: File): BackupData {
        val json = if (file.name.endsWith(".zip")) {
            ZipFile(file).use { zip ->
                val entry = zip.getEntry(BACKUP_JSON_ENTRY) ?: error("备份 zip 中未找到 backup.json")
                zip.getInputStream(entry).readBytes().toString(Charsets.UTF_8)
            }
        } else {
            FileInputStream(file).readBytes().toString(Charsets.UTF_8)
        }
        return BackupJson.decode(json, LlmConfigStore::readConfigJson)
    }

    companion object {
        private val stampFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.ROOT)
        private val fileStamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.ROOT)

        fun directory(root: File) = File(root, "CatClawMusic")

        /** zip 与旧版裸 json 都算备份文件，按名字倒序 = 新的在前 */
        fun list(root: File): List<File> =
            directory(root).listFiles()?.filter {
                it.name.startsWith("backup_") && (it.name.endsWith(".zip") || it.name.endsWith(".json"))
            }?.sortedByDescending { it.name } ?: emptyList()

        fun readInfo(root: File, file: File): BackupData? = runCatching {
            val json = if (file.name.endsWith(".zip")) {
                ZipFile(file).use { zip ->
                    val entry = zip.getEntry(BACKUP_JSON_ENTRY) ?: return@use null
                    zip.getInputStream(entry).readBytes().toString(Charsets.UTF_8)
                }
            } else {
                file.readText()
            }
            json?.let { BackupJson.decode(it, LlmConfigStore::readConfigJson) }
        }.getOrNull()

        fun sanitize(name: String): String {
            val cleaned = buildString(name.length) {
                name.forEach { append(if (it in '0'..'9' || it.isLetter() || it in "._- ") it else '_') }
            }
            return cleaned.takeIf { it.isNotBlank() } ?: "unknown"
        }
    }
}

private fun report(onProgress: (BackupProgress) -> Unit, percent: Int, message: String) = onProgress(BackupProgress(percent, message))

private fun Int.has(flag: Int) = this and flag != 0

private fun songKey(title: String, artist: String) = "${title.trim()}|${artist.trim()}".lowercase(Locale.ROOT)
