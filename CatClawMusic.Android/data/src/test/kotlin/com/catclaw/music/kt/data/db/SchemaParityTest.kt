package com.catclaw.music.kt.data.db

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * 建表保真度闸门：把 Room 导出的 schema 与 MAUI 线上库导出的真实 DDL 逐表逐列比对。
 * 参考文件来自 `%LOCALAPPDATA%\CatClawMusic.Maui\...\catclaw.db` 的 sqlite_master，
 * 不是照 C# 代码猜出来的，所以这张表能挡住字段名写错、漏表、漏列。
 */
class SchemaParityTest {

    @Test
    fun roomSchemaMatchesMauiReference() {
        val reference = locate("catclaw.reference.sqlite.sql")?.readText()
            ?: throw IllegalStateException("找不到 docs-schema/catclaw.reference.sqlite.sql")
        val exported = requireNotNull(findRoomSchema()) { "Room 未导出 schema JSON，检查 room.schemaLocation" }.readText()

        val expected = referenceTables(reference)
        val actual = roomTables(exported)

        assertEquals("表集合必须与线上库一致", expected.keys.sorted(), actual.keys.sorted())
        assertTrue("参考 DDL 应含 17 张业务表，实际 ${expected.size}", expected.size == 17)

        for ((table, columns) in expected) {
            assertEquals("$table 列不一致", columns, actual[table])
        }
    }

    /** Room 2.8 把 schema 写到 schemas/<FQCN>/<version>.json；工作目录可能是 :data 或仓库根，两种都找 */
    private fun findRoomSchema(): File? {
        val roots = listOf(File("schemas"), File("data/schemas"), File("../data/schemas"))
        return roots.filter { it.isDirectory }
            .flatMap { root -> root.walkTopDown().filter { it.extension == "json" }.toList() }
            .firstOrNull()
    }

    private fun locate(name: String): File? =
        listOf(
            File("../docs-schema/$name"),
            File("docs-schema/$name"),
            File("../../docs-schema/$name"),
        ).firstOrNull { it.isFile }

    private fun referenceTables(sql: String): Map<String, Set<String>> {
        val result = LinkedHashMap<String, Set<String>>()
        val marker = Regex("CREATE TABLE\\s+\"?(\\w+)\"?\\s*\\(", RegexOption.IGNORE_CASE)
        for (match in marker.findAll(sql)) {
            val table = match.groupValues[1]
            // sqlite_sequence 是 SQLite 为 AUTOINCREMENT 自动维护的内部表，Room 同样会自动产生
            if (table == "sqlite_sequence") continue
            val open = match.range.last
            val close = matchingParen(sql, open) ?: continue
            result[table] = columnNames(sql.substring(open + 1, close))
        }
        return result
    }

    /** 顶层逗号切分，跳过括号内的内容（类型参数、DEFAULT 表达式） */
    private fun columnNames(body: String): Set<String> {
        val parts = mutableListOf<String>()
        var depth = 0
        val current = StringBuilder()
        for (ch in body) {
            when (ch) {
                '(' -> {
                    depth++; current.append(ch)
                }
                ')' -> {
                    depth--; current.append(ch)
                }
                ',' -> if (depth == 0) {
                    parts += current.toString(); current.clear()
                } else {
                    current.append(ch)
                }
                else -> current.append(ch)
            }
        }
        if (current.isNotBlank()) parts += current.toString()

        return parts.map { it.trim() }
            .filterNot { it.startsWith("PRIMARY", true) || it.startsWith("UNIQUE", true) ||
                it.startsWith("CHECK", true) || it.startsWith("FOREIGN", true) }
            .map { it.substringBefore(' ').trim('"', '`', '[') }
            .filter { it.isNotEmpty() }
            .toSet()
    }

    private fun matchingParen(text: String, openIndex: Int): Int? {
        var depth = 0
        for (i in openIndex until text.length) {
            when (text[i]) {
                '(' -> depth++
                ')' -> {
                    depth--
                    if (depth == 0) return i
                }
            }
        }
        return null
    }

    private fun roomTables(json: String): Map<String, Set<String>> {
        val result = LinkedHashMap<String, Set<String>>()
        json.split("\"tableName\":").drop(1).forEach { chunk ->
            val name = Regex("\"([A-Za-z0-9_]+)\"").find(chunk)?.groupValues?.get(1) ?: return@forEach
            val fields = chunk.substringBefore("\"indices\"", chunk)
            val columns = Regex("\"columnName\":\\s*\"([^\"]+)\"").findAll(fields)
                .map { it.groupValues[1] }.toSet()
            result[name] = columns
        }
        return result
    }
}
