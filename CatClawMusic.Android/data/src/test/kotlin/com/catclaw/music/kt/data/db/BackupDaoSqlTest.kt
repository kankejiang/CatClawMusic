package com.catclaw.music.kt.data.db

import java.io.File
import java.sql.Connection
import java.sql.DriverManager
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

/**
 * 把 [BackupDao] 那批备份专用 SQL 拿去**真 SQLite** 上跑。
 *
 * 为什么这几条必须实测而不是靠看：备份恢复是「错了不报错、只是数据不对」的典型 ——
 * 只补空字段的 `COALESCE(NULLIF(...))` 写反一次，就会把用户自己填过的艺术家资料盖掉；
 * `INSERT OR IGNORE` 依赖的唯一索引要是没建，恢复一次就多一倍重复条目。
 * 建表沿用仓库里那份从本机真实库导出的 schema，顺带钉住「测试环境 == 线上 schema」。
 */
class BackupDaoSqlTest {

    private lateinit var db: Connection

    @Before
    fun setUp() {
        db = DriverManager.getConnection("jdbc:sqlite::memory:")
        schema().split(';').map { it.trim() }
            .filter { it.isNotEmpty() && !it.contains("sqlite_sequence", ignoreCase = true) }
            .forEach { db.createStatement().execute(it) }
        seed()
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

    private fun seed() = db.createStatement().apply {
        execute("INSERT INTO Artists(Id, Name, Gender, Birthday, Region, Description) VALUES (1, '周杰伦', '', '1979', '', '')")
        execute("INSERT INTO Artists(Id, Name, Gender, Birthday, Region, Description) VALUES (2, '林俊杰', '男', '', '', '已填过的简介')")
        execute("INSERT INTO Songs(Id, Title, ArtistId, FilePath) VALUES (11, '夜曲', 1, '/m/11.mp3')")
        execute("INSERT INTO Songs(Id, Title, ArtistId, FilePath) VALUES (12, '她说', 2, '/m/12.mp3')")
        execute("INSERT INTO Songs(Id, Title, FilePath) VALUES (13, '纯音乐无名', '/m/13.mp3')")
        execute("INSERT INTO Playlists(Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem, SortOrder) VALUES (7, '深夜歌单', 1, 2, 2, 0, 0)")
        execute("INSERT INTO PlaylistSongs(PlaylistId, SongId, Position) VALUES (7, 12, 0), (7, 11, 1)")
        execute("INSERT INTO Favorites(SongId, AddedAt) VALUES (11, 100)")
        execute("INSERT INTO PlayHistory(SongId, PlayedAt, PlayCount) VALUES (11, 100, 3)")
    }

    private fun rows(sql: String): List<Map<String, String?>> {
        val rs = db.createStatement().executeQuery(sql)
        val out = mutableListOf<Map<String, String?>>()
        while (rs.next()) {
            val map = LinkedHashMap<String, String?>()
            for (i in 1..rs.metaData.columnCount) map[rs.metaData.getColumnLabel(i)] = rs.getString(i)
            out += map
        }
        return out
    }

    @Test
    fun `备份歌单条目带出歌曲标题与歌手，并按位置定序`() {
        val got = rows(SQL_BACKUP_PLAYLIST_SONGS)
        assertEquals(listOf("12", "11"), got.map { it["songId"] })
        assertEquals(listOf("她说", "夜曲"), got.map { it["songTitle"] })
        // 歌手来自 Artists 关联，无关联时 IFNULL 兜成空串而不是 null
        assertEquals(listOf("林俊杰", "周杰伦"), got.map { it["songArtist"] })
        assertEquals(listOf("0", "1"), got.map { it["position"] })
    }

    @Test
    fun `只补空字段，用户填过的绝不覆盖`() {
        db.prepareStatement(SQL_FILL_ARTIST).apply {
            setString(1, "性别"); setString(2, "生日"); setString(3, "地区"); setString(4, "简介"); setString(5, "周杰伦")
        }.execute()
        val filled = rows("SELECT Gender, Birthday, Region, Description FROM Artists WHERE Name = '周杰伦'").single()
        assertEquals(mapOf("Gender" to "性别", "Birthday" to "1979", "Region" to "地区", "Description" to "简介"), filled)

        // 已有内容的行：四个字段都得原样留着
        db.prepareStatement(SQL_FILL_ARTIST).apply {
            setString(1, "别人性别"); setString(2, "别的生日"); setString(3, "别的地区"); setString(4, "别的简介"); setString(5, "林俊杰")
        }.execute()
        val kept = rows("SELECT Gender, Birthday, Region, Description FROM Artists WHERE Name = '林俊杰'").single()
        assertEquals(
            "非空的性别与简介必须原样留着，空的三项按规则补上",
            mapOf("Gender" to "男", "Birthday" to "别的生日", "Region" to "别的地区", "Description" to "已填过的简介"),
            kept,
        )
    }

    @Test
    fun `重复条目由唯一索引与 OR IGNORE 挡掉，位置能从末尾续`() {
        db.prepareStatement(SQL_INSERT_PLAYLIST).apply { setString(1, "恢复的歌单"); setLong(2, 555L) }.execute()
        val newId = rows("SELECT Id FROM Playlists WHERE Name = '恢复的歌单'").single().getValue("Id")
        assertEquals("0", rows("SELECT SongCount FROM Playlists WHERE Id = $newId").single().getValue("SongCount"))
        db.prepareStatement(SQL_INSERT_PLAYLIST_SONG).apply {
            setString(1, newId); setString(2, "11"); setInt(3, 0)
        }.execute()
        // 再插一次同一条：库上有 ux_playlist_songs_playlist_song，OR IGNORE 让它静默跳过而不是翻倍。
        // 把 OR IGNORE 改成 INSERT 这条就会红 —— 它钉的是"恢复同一个备份两次不会把歌单撑爆"。
        db.prepareStatement(SQL_INSERT_PLAYLIST_SONG).apply {
            setString(1, newId); setString(2, "11"); setInt(3, 1)
        }.execute()
        assertEquals("1", rows("SELECT COUNT(*) AS c FROM PlaylistSongs WHERE PlaylistId = $newId").single().getValue("c"))
        assertEquals(listOf("11"), rows(SQL_EXISTING_SONG_IDS.replace(":playlistId", newId!!)).map { it.getValue("SongId") })
        // 换一首再插：位置由调用方从现有末尾续起，这里确认第二首能进
        db.prepareStatement(SQL_INSERT_PLAYLIST_SONG).apply {
            setString(1, newId); setString(2, "12"); setInt(3, 1)
        }.execute()
        assertEquals(listOf("11", "12"), rows(SQL_EXISTING_SONG_IDS.replace(":playlistId", newId)).map { it.getValue("SongId") })
    }

    @Test
    fun `收藏与历史的重复写入语义：收藏幂等、历史按次追加`() {
        db.prepareStatement(SQL_INSERT_FAVORITE).apply { setString(1, "11"); setLong(2, 999L) }.execute()
        assertEquals("1", rows("SELECT COUNT(*) AS c FROM Favorites WHERE SongId = 11").single().getValue("c"))

        db.prepareStatement(SQL_INSERT_HISTORY).apply { setString(1, "11"); setLong(2, 999L); setInt(3, 4) }.execute()
        assertEquals(
            listOf("3", "4"),
            rows("SELECT PlayCount FROM PlayHistory WHERE SongId = 11 ORDER BY PlayedAt").map { it["PlayCount"] },
        )
    }

    @Test
    fun `歌曲索引对无歌手的歌给空串，恢复侧才能按 标题+歌手 配对`() {
        val refs = rows(SQL_BACKUP_SONG_REFS).associate { it.getValue("title") to it.getValue("artist") }
        assertEquals("周杰伦", refs["夜曲"])
        assertEquals("", refs["纯音乐无名"])
        assertTrue(refs.keys.containsAll(listOf("夜曲", "她说", "纯音乐无名")))
    }
}
