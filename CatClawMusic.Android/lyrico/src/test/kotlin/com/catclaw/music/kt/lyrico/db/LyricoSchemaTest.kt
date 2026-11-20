package com.catclaw.music.kt.lyrico.db

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * `lyrico.db` 的 schema 守护。
 *
 * 与 `catclaw.db` 的 `SchemaParityTest` 是两回事：那张表要跟 MAUI 参考库逐列一致，
 * 这张表是我们自己移植 Lyrico 用的，只需要**冻结下来**——字段名/表名一改就让测试红，
 * 逼着改的人明确意识到「这会影响已安装用户的数据」，从而配套写迁移。
 *
 * 表结构来源：Room 导出的 `schemas/.../1.json`（ksp 的 room.schemaLocation）。
 */
class LyricoSchemaTest {

    @Test
    fun schemaIsFrozen() {
        val json = findSchema()?.readText()
            ?: throw IllegalStateException("找不到 lyrico.db 的 schema JSON，检查 room.schemaLocation")

        val tables = extractTables(json)
        val expected = mapOf(
            "lyrico_app_log" to setOf("id", "timestamp", "level", "tag", "message"),
            "lyrico_custom_tag_key" to setOf("id", "name", "enabled"),
            "lyrico_folder" to setOf("id", "path", "displayName", "isSaf", "enabled", "addedAt"),
            "lyrico_plugin_subscription" to setOf("id", "url", "name", "lastCheckedAt"),
            "lyrico_song" to setOf(
                "id", "filePath", "title", "artist", "album", "albumArtist", "genre", "date",
                "language", "trackNumber", "discNumber", "durationMs", "bitrate", "sampleRate",
                "channels", "sizeBytes", "lastModified", "folderId", "lyricsOrigin",
                "lyricsUpdatedAt",
            ),
            "lyrico_source_plugin" to setOf(
                "id", "pluginId", "displayName", "author", "versionName", "versionCode",
                "entry", "manifestJson", "enabled", "installedAt", "updatedAt",
            ),
        )

        assertEquals("表集合被改动了", expected.keys.sorted(), tables.keys.sorted())
        for ((table, cols) in expected) {
            assertEquals("$table 的列被改动了", cols.sorted(), tables.getValue(table).sorted())
        }
        assertTrue("表数量应与冻结值一致：实际 ${tables.size}", tables.size == expected.size)
    }

    /** Room 2.8 写在 schemas/<FQCN>/<version>.json；工作目录可能是 :lyrico 或仓库根。 */
    private fun findSchema(): File? =
        listOf(File("schemas"), File("lyrico/schemas"), File("../lyrico/schemas"))
            .filter { it.isDirectory }
            .flatMap { root -> root.walkTopDown().filter { it.extension == "json" }.toList() }
            .firstOrNull()

    /**
     * 从 Room schema JSON 里抠出 table -> columns。
     *
     * 不引 JSON 库：这里只需要「表名 + 字段名」，正则足够，也避免测试对序列化实现的依赖。
     */
    private fun extractTables(json: String): Map<String, Set<String>> {
        val result = mutableMapOf<String, Set<String>>()
        // "tableName": "xxx", 之后紧跟该表的 field 列表
        val tableBlocks = Regex("\"tableName\"\\s*:\\s*\"([^\"]+)\"(.+?)(?=\"tableName\"|\\z)", RegexOption.DOT_MATCHES_ALL)
        for (m in tableBlocks.findAll(json)) {
            val name = m.groupValues[1]
            val body = m.groupValues[2]
            val columns = Regex("\"columnName\"\\s*:\\s*\"([^\"]+)\"").findAll(body)
                .map { it.groupValues[1] }
                .toSet()
            result[name] = columns
        }
        return result
    }
}
