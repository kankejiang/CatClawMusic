package com.catclaw.music.kt.core.library

/**
 * 扫描入库的纯规则（放 :core 以便 JVM 单测覆盖）：
 * MediaStore 字段 → 入库字段的换算、艺术家名归一、去重键、封面/歌词侧车判定。
 */
object MediaScanRules {

    const val UNKNOWN_ARTIST = "未知艺术家"
    const val UNKNOWN_ALBUM = "未知专辑"

    /** MediaStore 的 ARTIST 可能是 null、空串或 "<unknown>"，C# 侧统一显示「未知艺术家」 */
    fun normalizeArtist(raw: String?): String =
        raw?.trim()?.takeIf { it.isNotEmpty() && !it.equals("<unknown>", ignoreCase = true) } ?: UNKNOWN_ARTIST

    /**
     * 哪些已入库的歌算"文件没了"。
     *
     * `scannedAny=false` 时**必须返回空**：那次扫描一个都没返回，`seenPaths` 是空集，
     * 朴素的差集会把**整库**判成过期并全删。而"扫出 0 条"恰恰是最容易发生的状态 ——
     * 权限被撤、MediaProvider 短暂不可用、查询抛异常被上层吞成空列表，都会走到这条路上。
     *
     * 顺带记一句查证过的事实：MAUI 的本地扫描 `CatClawMusic.Data/MusicScanner.cs`（397 行）
     * 全文没有任何删歌曲逻辑，`RemoveStaleSongsAsync` 在生产代码里也只被网络扫描调用；
     * 也就是说这个本地清理是本端多加的，那它就只能比 MAUI 更保守，不能更激进。
     */
    fun stalePaths(existingPaths: Collection<String>, seenPaths: Set<String>, scannedAny: Boolean): List<String> {
        if (!scannedAny) return emptyList()
        return existingPaths.filter { it !in seenPaths }
    }

    /** 作用域存储下 DATA 列可能为 null，因此统一以 content URI 入库（与 SAF 歌曲同一做法） */
    fun contentUri(mediaStoreId: Long): String = "content://media/external/audio/media/$mediaStoreId"

    /** Duration 契约是「秒」；MediaStore 给的是毫秒 */
    fun durationSeconds(durationMs: Long): Int = (durationMs / 1000L).coerceIn(0, Int.MAX_VALUE.toLong()).toInt()

    /** 去重键：FilePath 唯一（线上库上就是 UQ(FilePath)） */
    fun dedupKey(filePath: String): String = filePath.trim()

    // 这里原来有一个只认 .lrc 的 `sidecarLyricsPath`，没有任何调用方（只有测试在调）。
    // 删掉而不是留着：远程侧车配对在 [SidecarRules]，本地找词在 `LyricsFileLocator`（集合更宽），
    // 三者并存时最容易被接错的是这个最窄的 —— 接错的症状是「.ttml 歌词不显示」，很难往这上面想。

    /**
     * 音频扩展名白名单，逐条抄自 C# `MusicUtility.AudioExtensions`（26 项）；
     * 远程曲库扫描与本地扫描共用这一份，别各写一套。
     */
    fun isAudioFile(nameOrPath: String): Boolean {
        val slash = nameOrPath.lastIndexOf('/')
        val name = if (slash >= 0) nameOrPath.substring(slash + 1) else nameOrPath
        val dot = name.lastIndexOf('.')
        if (dot <= 0 || dot == name.length - 1) return false
        return AUDIO_EXTENSIONS.contains(name.substring(dot + 1).lowercase())
    }

    /** 文件名（去扩展名）→ 标题，与 C# 远程扫描的 Title 取法一致 */
    fun titleFromFileName(nameOrPath: String): String {
        val slash = nameOrPath.lastIndexOf('/')
        val name = if (slash >= 0) nameOrPath.substring(slash + 1) else nameOrPath
        val dot = name.lastIndexOf('.')
        return if (dot > 0) name.substring(0, dot) else name
    }

    /** 父目录（去掉尾部斜杠），用于侧车文件配对 */
    fun parentDir(path: String): String = path.trimEnd('/').substringBeforeLast('/', "")

    private val AUDIO_EXTENSIONS = setOf(
        "mp3", "flac", "ogg", "oga", "opus", "m4a", "mp4", "aac", "wma",
        "wav", "aiff", "aifc", "ape", "wv", "tta", "mka", "dsf", "dff",
        "mid", "midi", "rmi", "spx", "amr", "3gp", "mkv", "webm",
    )

    /** 文件名 → 标题兜底（MediaStore 无 TITLE 时用） */
    fun titleFromPath(filePath: String): String {
        val name = filePath.substringAfterLast('/').substringBeforeLast('.')
        return name.ifEmpty { filePath }
    }

    /** 比特率：MediaStore 给 bps，入库契约是 kbps */
    fun bitrateKbps(bps: Int): Int = if (bps <= 0) 0 else bps / 1000

    /**
     * SAF 音乐文件夹清单的编解码（一个字符串存多棵树 URI）。
     *
     * 分隔符跟 C# 侧 `FolderPicker` 一样用 `|`：SAF 的 tree URI 里不会出现它，
     * 而 URI 里常见的 `,`、`:`、`/` 都不能拿来当分隔符。
     * 去空白 + 去重 + 保序，是因为这份清单会既作为存储值、又作为列表页的显示顺序。
     */
    fun encodeFolders(uris: Collection<String>): String =
        uris.map { it.trim() }.filter { it.isNotEmpty() }.distinct().joinToString("|")

    fun decodeFolders(stored: String): List<String> =
        stored.split('|').map { it.trim() }.filter { it.isNotEmpty() }.distinct()
}
