package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
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

    // ── songsOfArtist / songsOfAlbum ────────────────────────────

    /** `:artistId` 在 SQL 里出现两次：Room 按名字绑定会喂两处，JDBC 的 `?` 按位置，所以要逐个槽位绑 */
    private fun idsOf(sql: String, value: Int): List<Int> {
        val prepared = sql.replace(Regex(":(\\w+)"), "?")
        val slots = Regex("\\?").findAll(prepared).count()
        return db.prepareStatement(prepared).apply {
            (1..slots).forEach { setInt(it, value) }
        }.executeQuery().let { rs -> generateSequence { if (rs.next()) rs.getInt("id") else null }.toList() }
    }

    private fun link(songId: Int, artistId: Int) {
        db.prepareStatement("INSERT INTO SongArtists (SongId, ArtistId) VALUES (?,?)").apply {
            setInt(1, songId); setInt(2, artistId)
        }.executeUpdate()
    }

    private fun track(songId: Int, number: Int) {
        db.prepareStatement("UPDATE Songs SET TrackNumber = ? WHERE Id = ?").apply {
            setInt(1, number); setInt(2, songId)
        }.executeUpdate()
    }

    @Test
    fun artistSongsIncludeFeaturedLinksExactlyOnce() {
        addArtist(11, "主唱"); addArtist(12, "嘉宾"); addArtist(13, "别人")
        addSong(1, "Alpha", artistId = 11)
        addSong(2, "Beta", artistId = 13) // 主歌手是 13，但合作名单里有 11
        addSong(3, "Gamma", artistId = 12)
        link(2, 11); link(2, 13) // 同一首歌挂两个歌手：查 11 时不能因为 JOIN 出两行

        assertEquals("挂名合作要能查到，且一行只出一次", listOf(1, 2), idsOf(SQL_SONGS_OF_ARTIST, 11))
        assertEquals(listOf(3), idsOf(SQL_SONGS_OF_ARTIST, 12))
    }

    @Test
    fun artistSongsMatchByIdSoSameNameArtistsStayApart() {
        // C# 那句是 `WHERE a.Name = ? OR a2.Name = ?` —— 两个都叫 Jay 的人会并成一个人。
        // 这里按 Id 匹配，所以各查各的。
        addArtist(11, "Jay"); addArtist(12, "Jay")
        addSong(1, "Alpha", artistId = 11)
        addSong(2, "Beta", artistId = 12)

        assertEquals(listOf(1), idsOf(SQL_SONGS_OF_ARTIST, 11))
        assertEquals(listOf(2), idsOf(SQL_SONGS_OF_ARTIST, 12))
    }

    @Test
    fun artistSongsOfUnknownArtistAreEmptyRatherThanEverything() {
        addArtist(11, "主唱")
        addSong(1, "Alpha", artistId = 11)
        addSong(2, "无主歌手") // ArtistId 为 NULL：没有歌手归属，不该出现在任何人名下
        assertEquals(emptyList<Int>(), idsOf(SQL_SONGS_OF_ARTIST, 999))
        assertEquals(listOf(1), idsOf(SQL_SONGS_OF_ARTIST, 11))
    }

    @Test
    fun albumSongsOrderTaggedTracksFirstThenUntaggedByTitle() {
        addAlbum(21, "某张专辑"); addAlbum(22, "另一张")
        addSong(1, "Z-无轨号", albumId = 21)
        addSong(2, "第三轨", albumId = 21)
        addSong(3, "第一轨", albumId = 21)
        addSong(4, "A-无轨号", albumId = 21)
        addSong(5, "别的专辑", albumId = 22)
        track(2, 3); track(3, 1)

        assertEquals(
            "有轨号的按轨号排（1 轨、3 轨），没标签的落到后面按标题排 —— 入库顺序不等于曲目顺序",
            listOf(3, 2, 4, 1), idsOf(SQL_SONGS_OF_ALBUM, 21),
        )
        assertEquals(listOf(5), idsOf(SQL_SONGS_OF_ALBUM, 22))
    }

    @Test
    fun albumRowsCarryTheLowestTrackedSongAsTheCoverSource() {
        addAlbum(21, "一张专辑"); addAlbum(22, "空专辑")
        addSong(1, "第三轨", albumId = 21)
        addSong(2, "第一轨", albumId = 21)
        addSong(3, "没有轨号", albumId = 21)
        track(1, 3); track(2, 1)

        val samples = db.createStatement().executeQuery(SQL_ALBUMS).let { rs ->
            buildMap {
                while (rs.next()) put(rs.getString("title"), rs.getString("sampleFilePath"))
            }
        }
        assertEquals(
            "代表曲取最小曲目号的那首（有轨号的排在没轨号的前面）",
            "/m/2.mp3", samples["一张专辑"],
        )
        assertNull("专辑里没有歌时就是 null，不能凭空指一条路径", samples["空专辑"])
    }

    @Test
    fun playStatsCountsSongsOnceAndSumsThePlays() {
        recordPlay(7, 100L); recordPlay(7, 200L); recordPlay(8, 300L)
        val rs = db.createStatement().executeQuery(SQL_PLAY_STATS)
        assertTrue(rs.next())
        assertEquals("一首歌播两次只算一首播过的歌", 2, rs.getInt("songsPlayed"))
        assertEquals("总次数是各行 PlayCount 之和", 3, rs.getInt("totalPlays"))
    }

    @Test
    fun playStatsOnAnEmptyHistoryIsZeroNotNull() {
        // 空表时 SUM 返回 NULL 而不是 0，少了 COALESCE 就是发现页一进来就抛
        val rs = db.createStatement().executeQuery(SQL_PLAY_STATS)
        assertTrue(rs.next())
        assertEquals(0, rs.getInt("songsPlayed"))
        assertEquals(0, rs.getInt("totalPlays"))
    }

    @Test
    fun songDetailSumsPlaysAndToleratesMissingArtistAndAlbum() {
        addArtist(11, "周柏豪"); addAlbum(21, "8")
        addSong(1, "你还怕大雨吗", artistId = 11, albumId = 21)
        addSong(2, "无归属的歌")
        recordPlay(1, 100L)
        recordPlay(1, 200L)

        val rs = db.prepareStatement(SQL_SONG_DETAIL.replace(":id", "?")).apply { setInt(1, 1) }.executeQuery()
        assertTrue(rs.next())
        assertEquals("周柏豪", rs.getString("artist"))
        assertEquals("8", rs.getString("album"))
        assertEquals("详情里的播放次数是该歌所有历史行之和", 2, rs.getInt("playCount"))
        // 「查看歌手/查看专辑」要的是 id，不是名字——名字重名时会跳错页
        assertEquals(11, rs.getInt("artistId"))
        assertEquals(21, rs.getInt("albumId"))

        val orphan = db.prepareStatement(SQL_SONG_DETAIL.replace(":id", "?")).apply { setInt(1, 2) }.executeQuery()
        assertTrue(orphan.next())
        assertEquals("没有歌手/专辑时给出空串而不是 null 或 '未知'", "", orphan.getString("artist"))
        assertEquals(0, orphan.getInt("playCount"))
        assertEquals("无归属时 id 给 0，菜单据此不画「查看歌手」", 0, orphan.getInt("artistId"))
        assertEquals(0, orphan.getInt("albumId"))
    }

    @Test
    fun albumSongsNeedTheAlbumRowToExist() {
        addSong(1, "孤儿曲", albumId = 21) // AlbumId 指向一张已被清理掉的专辑
        assertEquals(
            "JOIN Albums 是内连接：专辑行没了就不出歌。宁可显示空专辑，也不要一排查不到归属的歌",
            emptyList<Int>(), idsOf(SQL_SONGS_OF_ALBUM, 21),
        )
    }

    // ── recordPlay ──────────────────────────────────────────────

    /** 与 `LibraryDao.recordPlay` 同一段读-改-写。PlayHistory 上没有唯一索引，"一行一歌"全靠这段维持 */
    private fun recordPlay(songId: Int, nowSeconds: Long) {
        val row = db.prepareStatement("SELECT Id, PlayCount FROM PlayHistory WHERE SongId = ? LIMIT 1")
            .apply { setInt(1, songId) }.executeQuery()
        val id: Int
        val count: Int
        row.apply { id = if (next()) getInt("Id") else -1; count = if (id > 0) getInt("PlayCount") else 0 }.close()
        if (id > 0) {
            // 具名参数按出现顺序换成位置参数：playedAt、playCount、id
            db.prepareStatement(
                SQL_TOUCH_HISTORY.replace(":playedAt", "?").replace(":playCount", "?").replace(":id", "?"),
            ).apply {
                setLong(1, nowSeconds); setInt(2, count + 1); setInt(3, id)
            }.executeUpdate()
        } else {
            db.prepareStatement("INSERT INTO PlayHistory (SongId, PlayedAt, PlayCount) VALUES (?,?,1)").apply {
                setInt(1, songId); setLong(2, nowSeconds)
            }.executeUpdate()
        }
    }

    private fun historyRows(): List<Triple<Int, Long, Int>> =
        db.createStatement().executeQuery("SELECT SongId, PlayedAt, PlayCount FROM PlayHistory ORDER BY SongId")
            .let { rs ->
                generateSequence {
                    if (rs.next()) Triple(rs.getInt("SongId"), rs.getLong("PlayedAt"), rs.getInt("PlayCount")) else null
                }.toList()
            }

    @Test
    fun recordPlayKeepsOneRowPerSongAndOnlyThenIncrements() {
        recordPlay(7, 100L)
        recordPlay(7, 200L)
        recordPlay(8, 300L)

        assertEquals(
            "同一首歌两次聆听必须落在同一行上刷新（否则常听排行会把重复行当多首歌）",
            listOf(Triple(7, 200L, 2), Triple(8, 300L, 1)), historyRows(),
        )
    }

    @Test
    fun trimHistoryKeepsTheMostRecentRows() {
        recordPlay(1, 10L); recordPlay(2, 20L); recordPlay(3, 30L); recordPlay(4, 40L)
        db.prepareStatement(SQL_TRIM_HISTORY.replace(":keep", "?")).apply { setInt(1, 2) }.executeUpdate()

        assertEquals(
            "裁剪按 PlayedAt 倒序保留：留下的是最新的两首，不是最早插入的两首",
            listOf(3, 4), historyRows().map { it.first },
        )
        assertEquals("留下的时间戳", listOf(30L, 40L), historyRows().map { it.second }.sorted())
    }
}
