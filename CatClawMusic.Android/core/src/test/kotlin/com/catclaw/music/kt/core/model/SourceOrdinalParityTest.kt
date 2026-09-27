package com.catclaw.music.kt.core.model

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 跨语言枚举保真闸门：Kotlin 侧 SongSource / ProtocolType 的**名字与顺序**必须和
 * C# `CatClawMusic.Core/Models/Song.cs`、`ConnectionProfile.cs` 完全一致 ——
 * 这两个枚举的 ordinal 就是 DB 里 Source / Protocol 列的整数值，
 * 一旦各自加值就会写出对方读不懂的行（本仓曾经就写成过 6 值版）。
 */
class SourceOrdinalParityTest {

    private val coreDir by lazy {
        listOf("../../CatClawMusic.Core", "../CatClawMusic.Core", "CatClawMusic.Core")
            .map { File(it) }
            .firstOrNull { it.isDirectory }
            ?: error("找不到 CatClawMusic.Core（测试工作目录应为 CatClawMusic.Android/core）")
    }

    @Test
    fun `SongSource 与 C# 逐位一致`() {
        assertEquals(csEnum("Song.cs", "SongSource"), SongSource.entries.map { it.name })
    }

    @Test
    fun `ProtocolType 与 C# 逐位一致`() {
        assertEquals(csEnum("ConnectionProfile.cs", "ProtocolType"), ProtocolType.entries.map { it.name })
    }

    /** 逐行读 C# 枚举体，按声明顺序返回成员名；显式 `= n` 必须等于它的位置 */
    private fun csEnum(fileName: String, enumName: String): List<String> {
        val text = File(coreDir, "Models/$fileName").readText(Charsets.UTF_8)
        val body = text.lineSequence()
            .dropWhile { !it.contains("enum $enumName") }
            .drop(1)
        val members = mutableListOf<String>()
        for (raw in body) {
            val line = raw.trim()
            if (line.isEmpty() || line.startsWith("///") || line == "{") continue
            if (line.startsWith("}")) break
            val name = line.substringBefore('=').trim().removeSuffix(",").trim()
            val explicit = line.substringAfter('=', "").trim().removeSuffix(",").toIntOrNull()
            assertTrue("enum $enumName 里解析出怪东西: $raw", name.all { it.isLetterOrDigit() || it == '_' })
            assertEquals("$enumName.$name 的显式值与位置不符", members.size, explicit ?: members.size)
            members += name
        }
        assertTrue("enum $enumName 解析为空", members.isNotEmpty())
        return members
    }
}
