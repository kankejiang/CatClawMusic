package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test

/**
 * 缓存字节数与播放会话裁剪，拿**真 SQLite** 跑。
 *
 * 挑这两条是因为：`cachedBytes()` 的值直接显示在设置页的"已用 X / 上限 N MB"上；
 * 而 `trimSessions` 藏着一个**方向会删反**的排序缺陷（见 `tieBreak` 那条）。
 */
class LibraryCacheSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        var dir = File(System.getProperty("user.dir"))
        val schema = generateSequence { dir.also { dir = dir.parentFile ?: return@generateSequence null } }
            .map { File(it, "docs-schema/catclaw.reference.sqlite.sql") }
            .firstOrNull { it.isFile }
            ?.readText() ?: error("找不到参考 schema")
        schema.split(';').map { it.trim() }
            .filter { it.isNotEmpty() && !it.contains("sqlite_sequence", ignoreCase = true) }
            .forEach { db.createStatement().execute(it) }
    }

    private fun bytes(sql: String): Long =
        db.prepareStatement(sql.replace(Regex(":(\\w+)"), "?")).executeQuery()
            .let { it.next(); it.getLong(1) }

    private fun addCached(id: Int, size: Long?) {
        db.prepareStatement("INSERT INTO CachedSongs (Id, SongId, LocalPath, CachedAt, FileSize) VALUES (?,?,?,?,?)").apply {
            setInt(1, id); setInt(2, id); setString(3, "/d/$id.mp3"); setLong(4, id * 10L)
            if (size == null) setNull(5, java.sql.Types.INTEGER) else setLong(5, size)
        }.executeUpdate()
    }

    private fun addSession(id: Int, playedAt: Long) {
        db.prepareStatement("INSERT INTO PlaySession (Id, SongId, PlayedAt, DurationMs) VALUES (?,?,?,?)").apply {
            setInt(1, id); setInt(2, 1); setLong(3, playedAt); setLong(4, 1000)
        }.executeUpdate()
    }

    private fun sessionIds(): List<Int> =
        db.prepareStatement("SELECT Id FROM PlaySession ORDER BY Id ASC").executeQuery()
            .let { rs -> generateSequence { if (rs.next()) rs.getInt(1) else null }.toList() }

    private fun trim(keep: Int, sql: String = SQL_TRIM_SESSIONS): Int =
        db.prepareStatement(sql.replace(Regex(":(\\w+)"), "?")).apply { setInt(1, keep) }.executeUpdate()

    @Test
    fun emptyCacheReportsZeroRatherThanNull() {
        // Room 侧返回类型是非空 Long：少了 COALESCE，空表时 SUM 给 NULL 就会抛
        assertEquals(0L, bytes(SQL_CACHED_BYTES))
    }

    @Test
    fun nullFileSizeRowsDoNotPoisonTheTotal() {
        addCached(1, 1000L)
        addCached(2, null) // 记账时大小未知的行
        addCached(3, 2500L)
        assertEquals("SUM 会跳过 NULL，COALESCE 只兜住全部为 NULL 的情况", 3500L, bytes(SQL_CACHED_BYTES))
    }

    @Test
    fun allNullSizesStillReportZero() {
        addCached(1, null)
        addCached(2, null)
        assertEquals(0L, bytes(SQL_CACHED_BYTES))
    }

    @Test
    fun trimIsANoOpUnderTheCap() {
        (1..3).forEach { addSession(it, it * 100L) }
        assertEquals("没超上限不该动任何行", 0, trim(2000))
        assertEquals(listOf(1, 2, 3), sessionIds())
    }

    @Test
    fun trimKeepsTheMostRecentByTimestamp() {
        (1..4).forEach { addSession(it, it * 100L) }
        assertEquals(2, trim(2))
        assertEquals(listOf(3, 4), sessionIds())
    }

    /**
     * 并列时的次级排序键。
     *
     * ⚠ 我原本断言"只按 `PlayedAt DESC` 会把最新那条删掉"—— **这个断言被真 SQLite 证伪了**：
     * 本机的排序器对 `ORDER BY x DESC` 的并列项实际按 rowid 倒序给，旧写法同样保留 [2,3]。
     * 所以这条测试钉的是"两种写法当前结果一致"，而加 `Id DESC` 的价值是
     * **把依赖排序器偶然行为变成显式保证**（SQL 标准里并列顺序是未定义的，换 SQLite 版本就可能变）。
     * 保留这条测试，是为了将来谁再声称"旧写法会删错"时，有人能看到它其实没有。
     */
    @Test
    fun sameMillisecondTiesResolveToTheNewestEitherWayButOnlyOneIsExplicit() {
        val old = """
            DELETE FROM PlaySession WHERE Id NOT IN (
                SELECT Id FROM PlaySession ORDER BY PlayedAt DESC LIMIT :keep
            )
        """
        (1..3).forEach { addSession(it, 7L) }
        trim(2, old)
        assertEquals("旧写法在本机也是留最新的（我原先的判断是错的）", listOf(2, 3), sessionIds())

        db.prepareStatement("DELETE FROM PlaySession").executeUpdate()
        (1..3).forEach { addSession(it, 7L) }
        trim(2)
        assertEquals("显式次级键与旧写法结果一致，区别是它不依赖排序器的偶然行为", listOf(2, 3), sessionIds())
    }
}
