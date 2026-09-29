package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * 把 `PlaylistDao` 里那几条"此前只能靠仪器化测试"的 SQL 拿去**真 SQLite** 上跑。
 *
 * 为什么不需要设备：Room 的 `@Query` 字符串已经提成 `SQL_*` 常量，注解与测试读的是**同一份**，
 * 所以这里执行的就是线上将被执行的那条语句本身，不是它的抄本 —— 抄本测试是假测试。
 *
 * 建表用的是仓库里那份 `docs-schema/catclaw.reference.sqlite.sql`（从本机真实 `catclaw.db` 的
 * `sqlite_master` 导出），于是这条链同时钉住三件事：参考 schema 里**唯一索引确实在**、
 * Room 依赖的语义成立、schema 换了这些测试会红。
 */
class PlaylistSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        // sqlite-jdbc 的 execute() 只跑第一条语句 —— 直接喂整个脚本会得到"no such table"，
        // 看着像 schema 坏了，其实是执行方式错了。这份参考 schema 里没有触发器/视图/字符串字面量，
        // 所以按分号切是安全的。
        // sqlite_sequence 由 SQLite 自己维护，参考 schema 里那条显式建表要跳过
        // （SchemaParityTest 也排除它，理由同一句话：它是 SQLite 内部表）
        referenceSchema().split(';').map { it.trim() }
            .filter { it.isNotEmpty() && !it.contains("sqlite_sequence", ignoreCase = true) }
            .forEach { stmt ->
            db.createStatement().execute(stmt)
        }
    }

    private fun referenceSchema(): String {
        var dir = File(System.getProperty("user.dir"))
        repeat(4) {
            val candidate = File(dir, "docs-schema/catclaw.reference.sqlite.sql")
            if (candidate.isFile) return candidate.readText()
            dir = dir.parentFile ?: return@repeat
        }
        error("找不到参考 schema（docs-schema/catclaw.reference.sqlite.sql）")
    }

    private fun insertPlaylist(id: Int, name: String, isSystem: Int) {
        db.prepareStatement(
            "INSERT INTO Playlists (Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem, SortOrder) " +
                "VALUES (?,?,?,?,?,?,?)",
        ).apply {
            setInt(1, id); setString(2, name); setLong(3, 1L); setLong(4, 1L)
            setInt(5, 0); setInt(6, isSystem); setInt(7, 0)
        }.executeUpdate()
    }

    /** Room 用 `:name` 占位，JDBC 用 `?` —— 只换占位符，语句其余部分一字不动 */
    private fun jdbc(sql: String): String = sql.replace(Regex(":(\\w+)"), "?")

    private fun insertItem(playlistId: Int, songId: Int, position: Int): Int =
        db.prepareStatement(jdbc(SQL_INSERT_ITEM)).apply {
            setInt(1, playlistId); setInt(2, songId); setInt(3, position)
        }.executeUpdate()

    private fun songIdsInOrder(playlistId: Int): List<Int> =
        db.prepareStatement(jdbc(SQL_SONG_IDS_IN_ORDER)).apply { setInt(1, playlistId) }.executeQuery()
            .let { rs -> generateSequence { if (rs.next()) rs.getInt(1) else null }.toList() }

    @Test
    fun referenceSchemaActuallyCarriesTheUniqueIndex() {
        val names = db.prepareStatement("PRAGMA index_list(PlaylistSongs)").executeQuery().let { rs ->
            generateSequence { if (rs.next()) rs.getString("name") else null }.toList()
        }
        assertTrue(
            "INSERT OR IGNORE 的去重全靠这个唯一索引；schema 里没有它，第 2 条测试就是在测一个不存在的保证：$names",
            names.any { it == "ux_playlist_songs_playlist_song" },
        )
    }

    @Test
    fun deleteCannotRemoveASystemPlaylist() {
        insertPlaylist(1, "我喜欢的音乐", isSystem = 1)
        insertPlaylist(2, "自建单", isSystem = 0)

        val affectedSystem = db.prepareStatement(jdbc(SQL_DELETE_PLAYLIST)).apply { setInt(1, 1) }.executeUpdate()
        assertEquals("内置单必须删不动 —— 保护写在 SQL 里，不靠调用方自觉", 0, affectedSystem)
        assertTrue(playlistExists(1))

        assertEquals(1, db.prepareStatement(jdbc(SQL_DELETE_PLAYLIST)).apply { setInt(1, 2) }.executeUpdate())
        assertEquals("自建单要能删", false, playlistExists(2))
    }

    @Test
    fun renameTouchesOnlyUserPlaylistsAndRefreshesUpdatedAt() {
        insertPlaylist(1, "我喜欢的", isSystem = 1)
        insertPlaylist(2, "公路", isSystem = 0)

        // 内置单改不动：页面上那两个入口的名字是写死的，表里改了名就和卡片对不上了
        val affectedSystem = db.prepareStatement(jdbc(SQL_RENAME_PLAYLIST)).apply {
            setString(1, "不该生效"); setLong(2, 999L); setInt(3, 1)
        }.executeUpdate()
        assertEquals(0, affectedSystem)
        assertEquals("我喜欢的", nameOf(1))

        assertEquals(1, db.prepareStatement(jdbc(SQL_RENAME_PLAYLIST)).apply {
            setString(1, "夜间驾驶"); setLong(2, 5_000L); setInt(3, 2)
        }.executeUpdate())
        assertEquals("夜间驾驶", nameOf(2))
        assertEquals("改名要顺带刷新 UpdatedAt，列表按它排序", 5_000L, updatedAtOf(2))

        assertEquals("id 不存在时改 0 行，调用方据此判断没改成", 0, db.prepareStatement(jdbc(SQL_RENAME_PLAYLIST)).apply {
            setString(1, "x"); setLong(2, 1L); setInt(3, 404)
        }.executeUpdate())
    }

    private fun nameOf(id: Int): String =
        db.prepareStatement("SELECT Name FROM Playlists WHERE Id = ?").apply { setInt(1, id) }
            .executeQuery().let { it.next(); it.getString(1) }

    private fun updatedAtOf(id: Int): Long =
        db.prepareStatement("SELECT UpdatedAt FROM Playlists WHERE Id = ?").apply { setInt(1, id) }
            .executeQuery().let { it.next(); (it.getObject(1) as Number).toLong() }

    private fun playlistExists(id: Int): Boolean =
        db.prepareStatement("SELECT 1 FROM Playlists WHERE Id = ?").apply { setInt(1, id) }.executeQuery()
            .let { it.next() }

    @Test
    fun duplicateItemIsIgnoredRatherThanThrowing() {
        insertPlaylist(7, "Road", isSystem = 0)
        assertEquals(1, insertItem(7, 100, 0))
        assertEquals("第二次插同一首歌应被唯一索引忽略而不是抛", 0, insertItem(7, 100, 1))
        assertEquals(1, insertItem(7, 101, 1))
        assertEquals(listOf(100, 101), songIdsInOrder(7))
    }

    @Test
    fun removeDeletesOnlyThatSongFromThatPlaylist() {
        insertPlaylist(7, "Road", isSystem = 0)
        insertPlaylist(8, "Night", isSystem = 0)
        insertItem(7, 100, 0)
        insertItem(7, 101, 1)
        insertItem(8, 100, 0)

        // 占位符顺序按 SQL 里出现的先后：(:playlistId, :songId)
        assertEquals(1, db.prepareStatement(jdbc(SQL_REMOVE_ITEM)).apply {
            setInt(1, 7); setInt(2, 100)
        }.executeUpdate())
        assertEquals("只该动 7 号歌单里的 100", listOf(101), songIdsInOrder(7))
        assertEquals("同一首歌在别的歌单里不受牵连", listOf(100), songIdsInOrder(8))
        assertEquals(
            "移一条不存在的条目要返回 0 行 —— DAO 的计数同步靠的是删完再数一遍，不是靠这里判断",
            0,
            db.prepareStatement(jdbc(SQL_REMOVE_ITEM)).apply { setInt(1, 7); setInt(2, 999) }.executeUpdate(),
        )
    }

    @Test
    fun orderFollowsPositionNotInsertionOrder() {
        insertPlaylist(7, "Road", isSystem = 0)
        insertItem(7, 10, 5)
        insertItem(7, 20, 0)
        insertItem(7, 30, 3)
        assertEquals("排序只认 Position：先插的不一定在前", listOf(20, 30, 10), songIdsInOrder(7))

        // 注意占位符顺序：Room 按 :name 绑定，所以 DAO 方法签名怎么写都行；
        // JDBC 按位置绑定，必须跟 SQL 里出现的顺序一致 —— 这条 SQL 是 (position, playlistId, songId)。
        assertEquals(1, db.prepareStatement(jdbc(SQL_UPDATE_POSITION)).apply {
            setInt(1, 9); setInt(2, 7); setInt(3, 20) // 把 20 从 Position 0 挪到 9
        }.executeUpdate())
        assertEquals("挪到末尾后顺序应随之改变", listOf(30, 10, 20), songIdsInOrder(7))
    }

    @Test
    fun appendContinuesFromCurrentMaxPosition() {
        insertPlaylist(7, "Road", isSystem = 0)
        insertItem(7, 1, 0)
        insertItem(7, 2, 1)
        val max = db.prepareStatement("SELECT MAX(Position) FROM PlaylistSongs WHERE PlaylistId = ?")
            .apply { setInt(1, 7) }.executeQuery()
        assertTrue(max.next())
        val next = max.getInt(1) + 1
        insertItem(7, 3, next)
        assertEquals("追加必须接在末尾；空表时 MAX 为 NULL 由代码回落成从 0 起", listOf(1, 2, 3), songIdsInOrder(7))
    }

    @Test
    fun emptyPlaylistHasNullMaxPosition() {
        insertPlaylist(7, "Road", isSystem = 0)
        val rs = db.prepareStatement("SELECT MAX(Position) FROM PlaylistSongs WHERE PlaylistId = ?")
            .apply { setInt(1, 7) }.executeQuery()
        assertTrue(rs.next())
        assertNull("appendAll 靠 `?: -1` 回落，前提是空表真返回 NULL 而不是 0", rs.getObject(1))
    }
}
