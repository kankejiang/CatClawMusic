package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test

/**
 * 发现页两条排序 SQL 拿去**真 SQLite** 上跑。
 *
 * 挑这两条是因为它们错起来不报错：JOIN 与 LIMIT 的先后、聚合排序的次级键，
 * 写歪了都只是"数据看着不太对"，编译期与 KSP 都拦不住。
 */
class LibraryDaoSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        schema().split(';').map { it.trim() }
            .filter { it.isNotEmpty() && !it.contains("sqlite_sequence", ignoreCase = true) }
            .forEach { db.createStatement().execute(it) }
    }

    @After
    fun tearDown() {
        runCatching { db.close() }
    }

    private fun schema(): String {
        var dir = File(System.getProperty("user.dir"))
        repeat(4) {
            val f = File(dir, "docs-schema/catclaw.reference.sqlite.sql")
            if (f.isFile) return f.readText()
            dir = dir.parentFile ?: return@repeat
        }
        error("找不到参考 schema")
    }

    private fun addHistory(songId: Int, playedAt: Long, playCount: Int) {
        db.prepareStatement("INSERT INTO PlayHistory (SongId, PlayedAt, PlayCount) VALUES (?,?,?)").apply {
            setInt(1, songId); setLong(2, playedAt); setInt(3, playCount)
        }.executeUpdate()
    }

    private fun ids(sql: String, limit: Int): List<Int> =
        db.prepareStatement(sql.replace(Regex(":(\\w+)"), "?")).apply { setInt(1, limit) }
            .executeQuery().let { rs -> generateSequence { if (rs.next()) rs.getInt("id") else null }.toList() }

    @Test
    fun recentSongsLimitsDistinctSongsNotHistoryRows() {
        addSong(1, "反复听的那首")
        addSong(2, "第二新")
        addSong(3, "第三新")
        // 歌 1 是最近播放的，且有 5 条历史；歌 2、3 各一条
        (1..5).forEach { addHistory(1, it * 10L, 1) } // 最后一次 50
        addHistory(2, 200L, 1)
        addHistory(3, 300L, 1)

        // 子查询先按"歌"去重再 LIMIT：最近的两首是 3 和 2（歌 1 的 50 比它们都旧）
        assertEquals(listOf(3, 2), ids(SQL_RECENT_SONGS, 2))

        // 把 LIMIT 挪到 JOIN 之后就会露馅：歌 1 的 5 条历史会占满名额
        addHistory(1, 999L, 1) // 让歌 1 变成最近
        val correct = ids(SQL_RECENT_SONGS, 2)
        assertEquals("名额按歌算，最近的两首各出现一次", listOf(1, 3), correct)
        assertEquals("结果里不该有重复歌曲", correct.size, correct.distinct().size)
    }

    @Test
    fun frequentSongsRanksByTotalThenByMostRecent() {
        addSong(1, "累计最高")
        addSong(2, "并列但更新")
        addSong(3, "并列但更旧")
        addHistory(1, 10L, 12)
        addHistory(2, 90L, 5)
        addHistory(2, 95L, 5) // 合计 10，最近一次 95
        addHistory(3, 20L, 10) // 合计 10，最近一次 20

        assertEquals(
            "SUM 并列时必须靠 MAX(PlayedAt) 定序，否则同一份数据每次排出来的顺序都可能不同",
            listOf(1, 2, 3), ids(SQL_FREQUENT_SONGS, 3),
        )
    }

    @Test
    fun frequentSongsIgnoresSongsWithNoHistory() {
        addSong(1, "听过"); addSong(2, "从没听过")
        addHistory(1, 1L, 3)
        assertEquals("JOIN 而非 LEFT JOIN：没播放历史的歌不该出现在常听里", listOf(1), ids(SQL_FREQUENT_SONGS, 10))
    }

    @Test
    fun recentSongsWithEmptyHistoryReturnsNothing() {
        addSong(1, "从没听过")
        assertEquals("没有播放历史就不该凭空冒出歌", emptyList<Int>(), ids(SQL_RECENT_SONGS, 5))
    }

    // ── searchSongs ─────────────────────────────────────────────

    private fun addArtist(id: Int, name: String) {
        db.prepareStatement("INSERT INTO Artists (Id, Name) VALUES (?,?)").apply {
            setInt(1, id); setString(2, name)
        }.executeUpdate()
    }

    private fun addAlbum(id: Int, title: String) {
        db.prepareStatement("INSERT INTO Albums (Id, Title) VALUES (?,?)").apply {
            setInt(1, id); setString(2, title)
        }.executeUpdate()
    }

    private fun addSong(id: Int, title: String, artistId: Int? = null, albumId: Int? = null, source: Int = 0) {
        db.prepareStatement("INSERT INTO Songs (Id, Title, FilePath, ArtistId, AlbumId, Source) VALUES (?,?,?,?,?,?)").apply {
            setInt(1, id); setString(2, title); setString(3, "/m/$id.mp3")
            artistId?.let { setInt(4, it) } ?: setNull(4, java.sql.Types.INTEGER)
            albumId?.let { setInt(5, it) } ?: setNull(5, java.sql.Types.INTEGER)
            setInt(6, source)
        }.executeUpdate()
    }

    /**
     * ⚠ 这条 SQL 里 `:query` 出现了**四次**（标题 / 歌手 / 专辑 Title / 专辑 Name 各一次）。
     * Room 按名字绑定，会把同一个参数喂给所有出现处；JDBC 的 `?` 是按位置的，
     * 只绑第 1 个会让剩下三个分支拿 NULL 去 LIKE —— 歌手/专辑搜索静默返回空，
     * 看着像 SQL 写错了，其实是测试的绑定方式错了。所以这里逐个位置绑同一个值。
     */
    private fun search(query: String): List<Int> {
        val sql = SQL_SEARCH_SONGS.replace(Regex(":(\\w+)"), "?")
        val slots = Regex("\\?").findAll(sql).count()
        return db.prepareStatement(sql).apply {
            (1..slots).forEach { setString(it, query) }
        }.executeQuery().let { rs -> generateSequence { if (rs.next()) rs.getInt("id") else null }.toList() }
    }

    @Test
    fun searchHitsTitleArtistOrAlbum() {
        addArtist(11, "周杰伦"); addAlbum(21, "十一月的萧邦")
        addSong(1, "夜曲", artistId = 11)          // 靠标题命中
        addSong(2, "Noseful Melody", artistId = 11) // 只靠歌手命中
        addSong(3, "Sunny", albumId = 21)           // 只靠专辑命中
        assertEquals(listOf(1), search("夜曲"))
        // 两首都挂在这个歌手下；`COLLATE NOCASE` 只折叠 ASCII 大小写，
        // 中文按 UTF-8 字节序排在 ASCII 之后，所以 "Noseful Melody" 在 "夜曲" 前面。
        assertEquals(listOf(2, 1), search("周杰伦"))
        assertEquals(listOf(3), search("十一月的萧邦"))
    }

    @Test
    fun searchIsCaseInsensitiveAndStableInOrdering() {
        addSong(1, "稻香")
        addSong(2, "ADORE")
        addSong(3, "adore")
        assertEquals("COLLATE NOCASE：大小写不同的同名歌要都能命中且顺序稳定", listOf(2, 3), search("adore"))
    }

    @Test
    fun searchPutsLocalSongsBeforeRemoteOnes() {
        // Source 的 ordinal 契约：Local=0、WebDAV=1、SMB=2、Cache=3（由 SourceOrdinalParityTest 钉住）
        addSong(1, "同一首歌", source = 1)
        addSong(2, "同一首歌", source = 0)
        assertEquals("本地曲优先是 `ORDER BY s.Source ASC` 的直接后果", listOf(2, 1), search("同一首歌"))
    }

    @Test
    fun wildcardCharactersInTheQueryReallyDoActAsWildcards() {
        addSong(1, "100% 纯音乐")
        addSong(2, "普通的歌")
        // 关键词是被拼进 LIKE 模式的（`'%' || :query || '%'`），所以 `%` 会被当通配符。
        // 这不是注入（值仍是绑定的），但它会让"搜 100%"把不相干的歌也捞进来 ——
        // 钉住现状而不是假装它不存在：要收紧就得转义 LIKE 的 % 与 _。
        assertEquals(listOf(1, 2), search("%"))
    }
}
