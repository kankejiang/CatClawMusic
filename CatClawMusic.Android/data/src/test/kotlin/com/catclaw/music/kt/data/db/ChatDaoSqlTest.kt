package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test

/**
 * 把 `ChatDao` 那三条分页/裁剪 SQL 拿去**真 SQLite** 上跑。
 *
 * 这几条的语义此前是靠推理修对过两次的（排序列该用 Id 还是 Timestamp、短页时返回什么、
 * 裁剪边界会不会把整表删空），而"靠推理对"正是最该被测试钉住的状态。
 * 建表用仓库里那份从本机真实库导出的 `docs-schema/catclaw.reference.sqlite.sql`，
 * 顺带把"测试环境 == 线上 schema"这一环也钉住。
 */
class ChatDaoSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        // sqlite-jdbc 的 execute() 只跑第一条语句，必须按分号切开逐条执行；
        // sqlite_sequence 是 SQLite 保留的内部表，显式建它会被拒。
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

    /** Room 按 `:name` 绑定、JDBC 按位置绑定 —— 这里只换占位符，语句其余部分一字不动 */
    private fun jdbc(sql: String): String = sql.replace(Regex(":(\\w+)"), "?")

    /** `timestamp` 故意可重复：同毫秒并列时才测得出排序到底靠谁 */
    private fun addRow(id: Int, role: String, timestamp: Long) {
        db.prepareStatement("INSERT INTO ChatMessageRecord (Id, Role, Content, Timestamp) VALUES (?,?,?,?)").apply {
            setInt(1, id); setString(2, role); setString(3, "c$id"); setLong(4, timestamp)
        }.executeUpdate()
    }

    private fun ids(sql: String, vararg args: Any): List<Int> =
        db.prepareStatement(jdbc(sql)).apply {
            args.forEachIndexed { index, value -> setInt(index + 1, value as Int) }
        }.executeQuery().let { rs -> generateSequence { if (rs.next()) rs.getInt(1) else null }.toList() }

    private fun recent(limit: Int) = ids(SQL_RECENT, limit)

    private fun olderThan(beforeId: Int, limit: Int) = ids(SQL_OLDER_THAN, beforeId, limit)

    private fun count(): Int =
        db.prepareStatement("SELECT COUNT(*) FROM ChatMessageRecord").executeQuery()
            .let { it.next(); it.getInt(1) }

    private fun trim(keep: Int): Int =
        db.prepareStatement(jdbc(SQL_TRIM_KEEPING_NEWEST)).apply { setInt(1, keep) }.executeUpdate()

    @Test
    fun recentReturnsTheNewestNButSortedOldestFirst() {
        (1..5).forEach { addRow(it, "user", it * 1000L) }
        assertEquals(
            "子查询取最近 3 条（Id DESC），外层要翻回升序给 UI 自上而下渲染 —— 方向反了聊天就会倒着长",
            listOf(3, 4, 5), recent(3),
        )
    }

    @Test
    fun shortPageReturnsEverythingRatherThanFailing() {
        addRow(1, "user", 1L)
        addRow(2, "assistant", 2L)
        assertEquals("不足一页时不能返回空", listOf(1, 2), recent(30))
        assertEquals(listOf(1, 2), olderThan(99, 30))
    }

    @Test
    fun identicalTimestampsAreStillOrderedByAutoId() {
        // 这就是排序列选 Id 而不是 Timestamp 的全部理由：同一毫秒连写两条时 Timestamp 并列
        (1..4).forEach { addRow(it, "user", 7L) }
        assertEquals(listOf(2, 3, 4), recent(3))
        assertEquals(listOf(1, 2), olderThan(3, 2))
    }

    @Test
    fun olderThanExcludesTheAnchorRow() {
        (1..5).forEach { addRow(it, "user", it * 1000L) }
        assertEquals("锚点本身不能重复出现在上一页，否则 UI 会渲染两条一样的", listOf(2, 3), olderThan(4, 2))
    }

    @Test
    fun trimKeepsTheNewestAndDeletesExactlyTheRest() {
        (1..5).forEach { addRow(it, "user", it * 1000L) }
        assertEquals(2, trim(3))
        assertEquals(listOf(3, 4, 5), recent(10))
    }

    @Test
    fun trimIsANoOpWhenUnderTheCap() {
        addRow(1, "user", 1L)
        addRow(2, "user", 2L)
        assertEquals("没超上限就该一行都不动", 0, trim(1000))
        assertEquals(2, count())
    }

    @Test
    fun trimWithZeroWipesTheTable() {
        (1..3).forEach { addRow(it, "user", it * 1000L) }
        // `Id NOT IN (空集)` 对所有行成立 ⇒ keep=0 会清空整表。
        // 这不是 bug，但它是调用方传错参数时的实际后果，所以钉住：ChatViewModel 的 1000 不能变成 0。
        assertEquals(3, trim(0))
        assertEquals(0, count())
    }
}
