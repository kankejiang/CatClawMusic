package com.catclaw.music.kt.core.lyrics

/** 取回一个 http(s) 资源的字节；拿不到就返回 null。真实实现走 okhttp，测试给假件。 */
fun interface RemoteBytesFetcher {
    suspend fun fetch(url: String): ByteArray?
}

/**
 * **远程侧车歌词**：WebDAV / SMB 入库时会给歌曲配一个远程 `.lrc` 地址存进 `Songs.LyricsPath`，
 * 这一条负责把它读出来。
 *
 * 为什么单独存在：C# `LyricsService.cs:214` 的注释写得很直白 ——
 * 「此前远程分支从不读 LyricsPath，导致 WebDAV/SMB 外挂歌词永远取不到」。
 * 本端在补这段之前是同一个症状：入库器存了地址，读歌词的人却只看本地目录，
 * 于是远程曲目的外挂歌词**配得上、显示不出**。
 *
 * 次序照 C#：远程侧车**优先于内嵌标签**（小文件直读，省一次 2 MB 的头请求）。
 * 任何一步失败都只返回 null，让上层继续往下走，不把歌词整条链路打断。
 */
class RemoteLyrics(private val fetch: RemoteBytesFetcher) {

    suspend fun load(lyricsPath: String?): Lyrics? {
        val url = lyricsPath?.takeIf { isRemoteUrl(it) } ?: return null
        val bytes = runCatching { fetch.fetch(url) }.getOrNull() ?: return null
        // 编码链自己带 2 MB 上限；这里提前挡，避免把超限文件整个解成字符串
        if (bytes.size > LyricsEncoding.MAX_LYRICS_FILE_BYTES) return null
        return runCatching { LyricsEncoding.parseContent(LyricsEncoding.decode(bytes)) }.getOrNull()
    }

    companion object {
        fun isRemoteUrl(path: String): Boolean =
            path.startsWith("http://", ignoreCase = true) || path.startsWith("https://", ignoreCase = true)
    }
}
