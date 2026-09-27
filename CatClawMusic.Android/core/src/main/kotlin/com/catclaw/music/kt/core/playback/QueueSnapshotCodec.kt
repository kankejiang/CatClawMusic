package com.catclaw.music.kt.core.playback

import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.model.SongSource

/** 键值存储抽象：Android 侧由 SharedPreferences 实现，单测用内存实现。 */
interface KeyValueStore {
    fun put(key: String, value: String)

    fun get(key: String): String?

    object Noop : KeyValueStore {
        override fun put(key: String, value: String) = Unit
        override fun get(key: String): String? = null
    }
}

/**
 * 队列快照编解码：一行一头 + 制表符分隔字段，字符串里的 `\t` `\n` 转义。
 * 不依赖 org.json，因此 :core 的纯 JVM 单测能直接覆盖。
 */
object QueueSnapshotCodec {
    private const val HEADER = "catclaw-queue-v1"
    private const val SEP = "\t"

    fun encode(snapshot: PlayQueue.Snapshot): String = buildString {
        append(HEADER).append(SEP).append(snapshot.currentIndex).append(SEP).append(snapshot.mode.name).append('\n')
        snapshot.songs.forEach { s ->
            append(s.id).append(SEP)
            append(esc(s.title)).append(SEP)
            append(esc(s.artist)).append(SEP)
            append(esc(s.album)).append(SEP)
            append(s.durationSeconds).append(SEP)
            append(esc(s.filePath)).append(SEP)
            append(s.source.name).append(SEP)
            append(esc(s.coverArtPath ?: ""))
            append('\n')
        }
    }

    fun decode(text: String): PlayQueue.Snapshot {
        val lines = text.split('\n').filter { it.isNotBlank() }
        if (lines.firstOrNull()?.startsWith(HEADER) != true) {
            return PlayQueue.Snapshot(emptyList(), -1, PlayMode.ListRepeat)
        }
        val head = lines[0].split(SEP)
        val currentIndex = head.getOrNull(1)?.toIntOrNull() ?: -1
        val mode = head.getOrNull(2)?.let { runCatching { PlayMode.valueOf(it) }.getOrNull() } ?: PlayMode.ListRepeat
        val songs = lines.drop(1).mapNotNull { line ->
            val f = line.split(SEP)
            if (f.size < 7) return@mapNotNull null
            Song(
                id = f[0].toIntOrNull() ?: return@mapNotNull null,
                title = unesc(f[1]),
                artist = unesc(f.getOrNull(2) ?: ""),
                album = unesc(f.getOrNull(3) ?: ""),
                durationSeconds = f.getOrNull(4)?.toIntOrNull() ?: 0,
                filePath = unesc(f[5]),
                source = f.getOrNull(6)?.let { runCatching { SongSource.valueOf(it) }.getOrNull() } ?: SongSource.Local,
                coverArtPath = unesc(f.getOrNull(7) ?: "").ifEmpty { null },
            )
        }
        return PlayQueue.Snapshot(songs, currentIndex, mode)
    }

    private fun esc(v: String) = v.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n")

    private fun unesc(v: String): String {
        val sb = StringBuilder(v.length)
        var i = 0
        while (i < v.length) {
            val c = v[i]
            if (c == '\\' && i + 1 < v.length) {
                when (val n = v[i + 1]) {
                    '\\' -> sb.append('\\')
                    't' -> sb.append('\t')
                    'n' -> sb.append('\n')
                    else -> sb.append(n)
                }
                i += 2
            } else {
                sb.append(c)
                i++
            }
        }
        return sb.toString()
    }
}
