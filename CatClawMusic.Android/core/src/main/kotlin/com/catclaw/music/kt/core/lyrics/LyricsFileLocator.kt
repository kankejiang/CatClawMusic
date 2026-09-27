package com.catclaw.music.kt.core.lyrics

import java.io.File

/** 侧车歌词文件定位，对齐 C# `TryReadLrcFileAsync` 的探测顺序与 MusicUtility.FindLyricsFile 的模糊匹配 */
object LyricsFileLocator {

    /** 文本类侧车：顺序即优先级 */
    val TEXT_EXTENSIONS = listOf("lrc", "ttml", "xml")

    /** 二进制侧车（酷狗 / QQ） */
    val BINARY_EXTENSIONS = listOf("krc", "qrc")

    /** 只查音频文件自己所在目录，不探 lyrics/ 子目录（与 C# 一致）；content:// 之类的 URI 没有目录概念 */
    fun directoryOf(audioPath: String): String? {
        if (audioPath.contains("://")) return null
        val normalized = audioPath.replace('\\', '/')
        val cut = normalized.lastIndexOf('/')
        if (cut <= 0) return null
        return normalized.substring(0, cut)
    }

    fun baseName(audioPath: String): String {
        val name = audioPath.replace('\\', '/').substringAfterLast('/')
        val dot = name.lastIndexOf('.')
        return if (dot > 0) name.substring(0, dot) else name
    }

    /** 精确同名侧车的候选路径，按优先级排列 */
    fun candidates(audioPath: String): List<String> {
        val dir = directoryOf(audioPath) ?: return emptyList()
        val base = baseName(audioPath)
        return (TEXT_EXTENSIONS + BINARY_EXTENSIONS).map { "$dir/$base.$it" }
    }

    /**
     * 模糊匹配：取第一个"文件名以歌名开头"的文件，优先级 .lrc &gt; .ttml &gt; .xml。
     * 传入的是同一目录下的文件名列表，便于纯单测覆盖。
     */
    fun fuzzyPick(fileNames: List<String>, songName: String): String? {
        if (songName.isBlank()) return null
        for (ext in TEXT_EXTENSIONS) {
            fileNames.firstOrNull { name ->
                name.substringBeforeLast('.').startsWith(songName) &&
                    name.substringAfterLast('.', "").equals(ext, ignoreCase = true)
            }?.let { return it }
        }
        return null
    }

    fun isBinaryLyrics(file: File): Boolean =
        BINARY_EXTENSIONS.any { it.equals(file.extension, ignoreCase = true) }
}
