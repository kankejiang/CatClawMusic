package com.catclaw.music.kt.data.backup

import com.catclaw.music.kt.core.ai.LlmConfig
import java.io.File
import java.util.zip.ZipFile
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import kotlinx.coroutines.runBlocking
import org.junit.Test

/**
 * 备份/恢复的**语义**闸门。用假 store 而不是 Room：这里要钉的是「备份文件里写了什么、
 * 恢复时怎么对回本地数据」，那部分才是跨端交换格式会出错的地方，而且错了不报错、只是数据不对。
 */
class BackupServiceTest {

    private fun runBlockingTest(block: suspend () -> Unit) = runBlocking { block() }

    private class FakeStore(
        var playlists: List<PlaylistBackupEntry> = emptyList(),
        var playlistSongs: List<PlaylistSongBackupEntry> = emptyList(),
        var history: List<PlayHistoryBackupEntry> = emptyList(),
        var favorites: List<FavoriteBackupEntry> = emptyList(),
        var artists: List<ArtistBackupEntry> = emptyList(),
        var covers: List<Pair<String, String>> = emptyList(),
        var songs: List<BackupStore.SongRef> = emptyList(),
        var configs: List<LlmConfig> = emptyList(),
        var current: String? = null,
        var chat: List<ChatMessageBackupEntry> = emptyList(),
        var aiMemory: String? = null,
    ) : BackupStore {
        val createdPlaylists = mutableListOf<String>()
        val addedItems = mutableListOf<Pair<Int, List<Int>>>()
        val playCounts = mutableMapOf<Int, Int>()
        val addedFavorites = mutableSetOf<Int>()
        val filledArtists = mutableListOf<ArtistBackupEntry>()
        val coverPaths = mutableMapOf<String, String>()
        var restoredConfigs: List<LlmConfig>? = null
        var replacedChat: List<ChatMessageBackupEntry>? = null
        var restoredMemory: String? = null

        override suspend fun playlists() = playlists
        override suspend fun playlistSongs() = playlistSongs
        override suspend fun playHistory() = history
        override suspend fun favorites() = favorites
        override suspend fun artists() = artists
        override suspend fun artistCoverFiles() = covers
        override suspend fun setArtistCover(artistName: String, path: String) { coverPaths[artistName] = path }
        override suspend fun artistNames() = artists.map { it.name }
        override suspend fun llmConfigs() = configs
        override suspend fun currentConfigName() = current
        override suspend fun chatMessages() = chat
        override suspend fun localSongs() = songs
        override suspend fun createPlaylist(name: String): Int { createdPlaylists += name; return 900 + createdPlaylists.size }
        override suspend fun addPlaylistSongs(playlistId: Int, songIds: List<Int>) { addedItems += playlistId to songIds }
        override suspend fun addPlayCounts(counts: Map<Int, Int>) { counts.forEach { (k, v) -> playCounts[k] = (playCounts[k] ?: 0) + v } }
        override suspend fun addFavorites(songIds: Collection<Int>) { addedFavorites += songIds }
        override suspend fun fillEmptyArtistFields(entries: List<ArtistBackupEntry>) { filledArtists += entries }
        override suspend fun restoreLlmConfigs(configs: List<LlmConfig>, currentName: String?) { restoredConfigs = configs; current = currentName }
        override suspend fun replaceChatMessages(messages: List<ChatMessageBackupEntry>) { replacedChat = messages }
        override suspend fun aiMemoryText(): String? = aiMemory
        override suspend fun restoreAiMemory(text: String): Int { restoredMemory = text; return 1 }
    }

    private val root = File(System.getProperty("java.io.tmpdir"), "catclaw-backup-test-${System.nanoTime()}")

    private fun service(store: BackupStore) = BackupService(store, File(root, "covers"), now = { 1_760_000_000_000 })

    private fun newStore() = FakeStore(
        playlists = listOf(PlaylistBackupEntry(id = 7, name = "深夜歌单", songCount = 2)),
        playlistSongs = listOf(
            PlaylistSongBackupEntry(7, 11, "夜曲", "周杰伦", 0),
            PlaylistSongBackupEntry(7, 12, "说好的幸福呢", "周杰伦", 1),
        ),
        history = listOf(PlayHistoryBackupEntry(11, "夜曲", "周杰伦", 3, 1L), PlayHistoryBackupEntry(11, "夜曲", "周杰伦", 2, 2L)),
        favorites = listOf(FavoriteBackupEntry(12, "说好的幸福呢", "周杰伦", 5L)),
        artists = listOf(ArtistBackupEntry("周杰伦", gender = "男", description = "华语歌手")),
        songs = listOf(BackupStore.SongRef(11, "夜曲", "周杰伦"), BackupStore.SongRef(12, "说好的幸福呢", "周杰伦")),
        configs = listOf(LlmConfig(name = "默认配置", model = "m1")),
        current = "默认配置",
        chat = listOf(ChatMessageBackupEntry(1, "user", "播放夜曲", 10L)),
    )

    @Test
    fun `备份产出单个 zip，里面有 json 主档与 covers 条目`() = runBlockingTest {
        root.mkdirs()
        val coverFile = File(root, "src/周杰伦.jpg").apply { parentFile!!.mkdirs() }
        coverFile.writeBytes(byteArrayOf(1, 2, 3))
        val store = newStore().also { it.covers = listOf("周杰伦" to coverFile.absolutePath) }
        val zip = service(store).backup(root)

        assertTrue(zip.name.startsWith("backup_"))
        assertTrue(zip.name.endsWith(".zip"))
        ZipFile(zip).use { archive ->
            val json = archive.getEntry("backup.json") ?: error("zip 里没有 backup.json")
            val text = archive.getInputStream(json).readBytes().toString(Charsets.UTF_8)
            val obj = Json.parseToJsonElement(text).jsonObject
            // 键名必须与 C# 的 camelCase 输出一字不差，否则跨端读不回
            listOf("version", "createdAt", "playlists", "playlistSongs", "playHistory", "favorites", "artists", "llmConfigs", "chatMessages")
                .forEach { assertTrue("缺键 $it", it in obj) }
            assertEquals("1.0", obj["version"]?.toString()?.trim('"'))
            assertTrue("歌单条目没写进去", text.contains("深夜歌单"))
            assertNotNull(archive.getEntry("covers/周杰伦.jpg"))
        }
        // 中间目录要清掉，别在备份目录里留一堆 _covers
        assertTrue(root.listFiles()!!.none { it.name.endsWith("_covers") })
    }

    @Test
    fun `记忆随备份带出，而对面没有记忆时不动本端`() = runBlockingTest {
        root.mkdirs()
        val source = newStore().also { it.aiMemory = """[{"Id":"a1","Content":"喜欢夜曲"}]""" }
        val zip = service(source).backup(root)
        ZipFile(zip).use { archive ->
            val text = archive.getInputStream(archive.getEntry("backup.json")!!).readBytes().toString(Charsets.UTF_8)
            assertTrue("键名要与 C# 一致", text.contains("aiMemoryContent"))
            assertTrue("内容要真的在里面", text.contains("喜欢夜曲"))
        }

        val target = FakeStore()
        service(target).restore(zip)
        assertEquals("恢复要交回同一段文本，转换细节归 store 自己", """[{"Id":"a1","Content":"喜欢夜曲"}]""", target.restoredMemory)

        // 对面那份备份里根本没有记忆段：拿它恢复不该把本端攒下的记忆抹平
        val blankZip = service(FakeStore()).backup(root)
        val keep = FakeStore()
        service(keep).restore(blankZip)
        assertNull("备份里没有记忆段就不该动本端", keep.restoredMemory)
    }

    @Test
    fun `恢复按 id 或 标题+歌手 对回本地歌，重复歌单名跳过`() = runBlockingTest {
        root.mkdirs()
        val source = newStore()
        val zip = service(source).backup(root)

        // 目标端：歌单同名的一条已存在，且 id 全对不上 → 只能靠 标题+歌手 找回
        val target = FakeStore(
            playlists = listOf(PlaylistBackupEntry(id = 1, name = "深夜歌单")),
            songs = listOf(BackupStore.SongRef(501, "夜曲", " 周杰伦 "), BackupStore.SongRef(502, "说好的幸福呢", "周杰伦")),
        )
        service(target).restore(zip)

        assertEquals("同名歌单不该重建", 0, target.createdPlaylists.size)
        assertEquals("同名之外的歌单不该插条目", 0, target.addedItems.size)

        val other = FakeStore(
            playlists = listOf(PlaylistBackupEntry(id = 1, name = "收藏向")),
            songs = target.songs,
        )
        service(other).restore(zip)
        assertEquals(listOf("深夜歌单"), other.createdPlaylists)
        assertEquals(listOf(501, 502), other.addedItems.single().second)
        // 同一首歌的多条历史要合并成一次写入
        assertEquals(mapOf(501 to 5), other.playCounts)
        assertEquals(setOf(502), other.addedFavorites)
        assertEquals(listOf("周杰伦"), other.filledArtists.map { it.name })
        assertEquals(listOf(LlmConfig(name = "默认配置", model = "m1")), other.restoredConfigs)
        assertEquals(1, other.replacedChat?.size)
    }

    @Test
    fun `读得懂 C# 写出来的 json：多余键与 null 都不炸`() = runBlockingTest {
        root.mkdirs()
        // 手抄一份 C# 侧的输出形态：带 null、带本端不认识的字段、数字写成字符串位
        val csharpJson = buildJsonObject {
            put("version", "1.0")
            put("createdAt", "2026-09-28T10:00:00")
            put("currentAgentId", "agent-1")
            put("aiMemoryContent", "# 记忆")
            put("playlists", Json.parseToJsonElement("""[{"id":3,"name":"跑步","songCount":1,"Extra":{"a":1}}]"""))
            put("playlistSongs", Json.parseToJsonElement("""[{"playlistId":3,"songId":41,"songTitle":"夜曲","songArtist":null,"position":0}]"""))
            put("playHistory", Json.parseToJsonElement("[]"))
            put("favorites", Json.parseToJsonElement("[]"))
            put("artists", Json.parseToJsonElement("[]"))
            put("llmConfigs", Json.parseToJsonElement("[]"))
            put("chatMessages", Json.parseToJsonElement("[]"))
        }
        val file = File(root, "from-pc.zip")
        java.util.zip.ZipOutputStream(file.outputStream()).use { zip ->
            zip.putNextEntry(java.util.zip.ZipEntry("backup.json"))
            zip.write(csharpJson.toString().toByteArray(Charsets.UTF_8))
            zip.closeEntry()
        }

        val target = FakeStore(songs = listOf(BackupStore.SongRef(41, "夜曲", "")))
        service(target).restore(file)
        assertEquals(listOf("跑步"), target.createdPlaylists)
        assertEquals(listOf(41), target.addedItems.single().second)
    }

    @Test
    fun `备份目录与列表按 C# 规则：CatClawMusic 下 backup_ 前缀，新的在前`() {
        val dir = BackupService.directory(root).apply { mkdirs() }
        File(dir, "backup_20260101_000000.zip").createNewFile()
        File(dir, "backup_20260909_000000.json").createNewFile()
        File(dir, "notes.txt").createNewFile()
        val names = BackupService.list(root).map { it.name }
        assertEquals(listOf("backup_20260909_000000.json", "backup_20260101_000000.zip"), names)
        assertFalse(BackupService.list(File(root, "nope")).any())
    }

    @Test
    fun `文件名清洗：非法字符换下划线，空名回落 unknown`() {
        assertEquals("周杰伦", BackupService.sanitize("周杰伦"))
        assertEquals("a_b_c", BackupService.sanitize("a/b:c"))
        assertEquals("unknown", BackupService.sanitize("   "))
    }
}
