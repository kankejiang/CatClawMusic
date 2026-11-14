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
            // MAUI 版队列快照只存 song id，不存会过期的直链；恢复后由插件实时取链。
            // 这里等价处理：网易云在线曲的 filePath 换成 outer 占位，恢复时
            // PlayerController.moveToCurrent 命中 outer 会走 uriUpgrader 实时取新链，
            // 避免把修复前缓存的 30s 试听 URL 永久写进快照（旧队列一直听到试听的根因）。
            append(esc(persistedPath(s))).append(SEP)
            append(s.source.name).append(SEP)
            append(esc(s.coverArtPath ?: "")).append(SEP)
            // 在线曲的来源标识（netease:xxx）：丢了它，恢复队列后歌词/取链都找不到原曲
            append(esc(s.remoteId ?: ""))
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
                remoteId = unesc(f.getOrNull(8) ?: "").ifEmpty { null }.takeIf { f.size >= 9 },
            )
        }
        return PlayQueue.Snapshot(songs, currentIndex, mode)
    }

    /** 网易云 CDN 直链（带时效，也可能只有 30s 试听）→ outer 占位；其它来源原样保存 */
    private fun persistedPath(s: Song): String {
        val remoteId = s.remoteId?.takeIf { it.startsWith("netease:") } ?: return s.filePath
        if (!s.filePath.contains("music.126.net")) return s.filePath
        return "https://music.163.com/song/media/outer/url?id=${remoteId.substringAfter(':')}.mp3"
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
