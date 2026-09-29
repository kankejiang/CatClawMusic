package com.catclaw.music.kt.core.library

import com.catclaw.music.kt.core.net.UrlCredentials

/**
 * 远程曲「补全元数据」的取值规则，逐条对齐 C# `NetworkMusicService.Metadata`。
 *
 * 为什么远程曲需要这一步：WebDAV 与 SMB 入库时只拿得到目录清单（文件名、大小、路径），
 * 艺术家、专辑、时长这些都在文件里，所以那批行是骨架 —— 列表上一眼望去全是 0:00 与空歌手。
 * C# 的补法是把远程文件的**头部字节**取回来解标签，本端把同一套判据原样搬过来。
 *
 * 放 `:core` 是因为这几条判据全是纯函数：哪个字段算「补到了」、占位值要不要顶掉真值、
 * 哪种格式值得再下一次大头。真正的取字节与落库在 `:data`。
 */
object MetadataBackfill {

    /** 小头 256 KB：够覆盖 ID3v2 加第一帧，也够 FLAC 的 STREAMINFO 与常规注释块 */
    const val TAG_HEAD_BYTES = 256 * 1024

    /** 大头 2 MB：FLAC 内嵌封面动辄超过 256 KB，小头读空就换这个再来一次 */
    const val TAG_HEAD_LARGE_BYTES = 2 * 1024 * 1024

    /** 有界并行的 worker 数，与 C# `MaxDegreeOfParallelism` 同值；再多远程会开始排队限流 */
    const val WORKERS = 4

    /** 每批落库的行数，与 C# `updated.Chunk(50)` 同值：一事务 50 行，取消时不至于白跑一整轮 */
    const val PERSIST_BATCH = 50

    /**
     * 「这首歌还缺元数据吗」。C# 的 WHERE 是 `Duration <= 0 OR ArtistId = 0`。
     *
     * ArtistId 是运行时字段：骨架行没有建艺术家关联，所以判「缺」看的是 id 为 0，
     * 而不是 Artists 表里那行叫什么名字。
     */
    fun needsBackfill(durationSeconds: Int, artistId: Int): Boolean = durationSeconds <= 0 || artistId == 0

    /**
     * 解出来的标签值不值得用 —— 决定要不要再花一次大头请求。
     * 对齐 C# `HasUsefulMetadata`：艺术家、专辑、时长有一个是真值就够。
     */
    fun hasUseful(tags: AudioTags): Boolean {
        val artist = tags.artist.orEmpty().trim()
        val album = tags.album.trim()
        return (artist.isNotEmpty() && artist != MediaScanRules.UNKNOWN_ARTIST) ||
            (album.isNotEmpty() && album != MediaScanRules.UNKNOWN_ALBUM) ||
            tags.durationMs > 0L
    }

    /** 取头部的顺序：只有易带大标签头的格式才排两次，否则一次小头就收工 */
    fun headSizes(pathOrUrl: String): List<Int> =
        if (mayHaveLargeTagHeader(pathOrUrl)) listOf(TAG_HEAD_BYTES, TAG_HEAD_LARGE_BYTES)
        else listOf(TAG_HEAD_BYTES)

    /** C# `MayHaveLargeTagHeader` 的那批扩展名 */
    private val LARGE_TAG_EXTENSIONS = setOf("flac", "ape", "wav", "aiff", "aif", "dsf", "wv")

    fun mayHaveLargeTagHeader(pathOrUrl: String): Boolean {
        val name = stripQuery(pathOrUrl).trimEnd('/').substringAfterLast('/')
        val dot = name.lastIndexOf('.')
        if (dot < 0 || dot == name.lastIndex) return false
        return name.substring(dot + 1).lowercase() in LARGE_TAG_EXTENSIONS
    }

    /**
     * 头 16 字节像不像音频。C# `IsAudioHeader` 用同一张表，目的是在解析之前挡掉
     * 半截下载、加密文件、伪装成音频的东西 —— 省掉一整轮「小头失败再下大头」的请求。
     */
    fun looksLikeAudio(head: ByteArray): Boolean {
        if (head.size < 4) return false
        val b = head
        fun ascii(at: Int, text: String): Boolean {
            if (at + text.length > b.size) return false
            for (i in text.indices) if (b[at + i] != text[i].code.toByte()) return false
            return true
        }
        // MP3 裸帧同步：11 位全 1
        if (b[0] == 0xFF.toByte() && b[1].toInt() and 0xE0 == 0xE0) return true
        return ascii(0, "ID3") || ascii(0, "fLaC") || ascii(0, "OggS") || ascii(4, "ftyp") ||
            (ascii(0, "RIFF") && ascii(8, "WAVE")) || ascii(0, "MAC ") || ascii(0, "wvpk") ||
            (ascii(0, "FORM") && ascii(8, "AIFF")) || ascii(0, "DSD ") || ascii(0, "MPCK")
    }

    /**
     * 把解到的标签合到已有行上，逐条对齐 C# `ApplyTagToSong`。
     *
     * 三处容易写错的地方，都钉了测试：
     * - 占位值不许顶掉真值（`未知艺术家` / `未知专辑` 是扫描时的填充词，不是信息）；
     * - 数值列只认「大于 0」，0 是「没解出来」而不是「真的是 0」；
     * - **类型列是无条件赋值**：没解到就把原来的清掉。这看着像 bug，但 C# 一直如此，
     *   而且类型不像时长那样有「0 等于未知」的约定，留着旧值反而会把改过的标签藏起来。
     *
     * `decodeTitle` 对应 C# 的 WebDAV 分支：远端标题可能带百分号编码。解码走
     * [UrlCredentials.percentDecode] 而不是 `URLDecoder` —— 后者会把 `+` 变成空格，
     * 标题里的加号（`Asia++` 这类艺名）会被改坏。
     */
    fun merge(current: AudioTags, fetched: AudioTags, decodeTitle: Boolean = false): AudioTags {
        val fetchedTitle = fetched.title.trim()
        val title = if (fetchedTitle.isEmpty() || fetchedTitle == current.title) {
            current.title
        } else if (!decodeTitle) {
            fetchedTitle
        } else {
            val decoded = UrlCredentials.percentDecode(fetchedTitle)
            if (decoded == current.title) current.title else decoded
        }

        val fetchedArtist = fetched.artist.orEmpty().trim()
        val artist = if (fetchedArtist.isEmpty() || fetchedArtist == MediaScanRules.UNKNOWN_ARTIST) {
            current.artist
        } else {
            fetchedArtist
        }

        val fetchedAlbum = fetched.album.trim()
        val album = if (fetchedAlbum.isEmpty() || fetchedAlbum == MediaScanRules.UNKNOWN_ALBUM) {
            current.album
        } else {
            fetchedAlbum
        }

        return current.copy(
            title = title,
            artist = artist,
            album = album,
            durationMs = if (fetched.durationMs > 0) fetched.durationMs else current.durationMs,
            bitrateKbps = if (fetched.bitrateKbps > 0) fetched.bitrateKbps else current.bitrateKbps,
            year = if (fetched.year > 0) fetched.year else current.year,
            trackNumber = if (fetched.trackNumber > 0) fetched.trackNumber else current.trackNumber,
            genre = fetched.genre,
        )
    }

    /**
     * 进度上报的节流：每 10 首或最后一首才报一次（C# 的 `d % 10 == 0 || d == total`）。
     * 一千首歌就是三百次重组件重绘，纯粹是浪费。
     */
    fun reportsProgress(done: Int, total: Int): Boolean = done % 10 == 0 || done == total

    /** 去掉查询串与片段：`/d/a.flac?sign=xx` 的扩展名判定不能看 sign */
    private fun stripQuery(value: String): String = value.substringBefore('#').substringBefore('?')
}
