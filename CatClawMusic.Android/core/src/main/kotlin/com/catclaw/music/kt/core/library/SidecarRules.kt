package com.catclaw.music.kt.core.library

/**
 * 远程曲库的**侧车配对**规则（歌词 / 封面）—— 从 `WebDavImporter` 里提出来，
 * 与本地那条 [LyricsFileLocator] 分开：两者扩展名集合**本来就不同**，混成一份才会出事。
 *
 * - 远程：入库时就把侧车 URL 写进 `LyricsPath` / `CoverArtPath`，只认 `.lrc`/`.ttml`，
 *   逐字对齐 C# `NetworkMusicService.Scan.cs` 那三处（WebDAV / SMB / 另一条协议路径各写了一遍，
 *   `ext is ".LRC" or ".TTML"`）。**故意不扩**：扩了就是与 MAUI 行为分叉，
 *   而用户看到的是「同一首歌在本地有词、在 WebDAV 没词」；
 * - 本地：播放时现找，走 `LyricsFileLocator`，集合更宽（含 .xml/.krc/.qrc，因为本端有对应的解析器）。
 *
 * 提出来是因为原来它写成一个私有函数躲在导入器里，而 `:core` 里另有一个**同名的窄规则**
 * （`MediaScanRules.sidecarLyricsPath`，只认 .lrc、且没有任何调用方）——
 * 将来有人接本地那条路时挑错一个，症状就是「.ttml 歌词不显示」。
 */
object SidecarRules {

    /** 远程入库时视为歌词的扩展名（C# `ext is ".LRC" or ".TTML"`） */
    val LYRICS_EXTENSIONS = setOf("lrc", "ttml")

    /** 目录内可当封面用的图片扩展名 */
    val IMAGE_EXTENSIONS = setOf("jpg", "jpeg", "png", "webp", "bmp")

    fun isLyricsSidecar(name: String): Boolean = extensionOf(name) in LYRICS_EXTENSIONS

    fun isImageSidecar(name: String): Boolean = extensionOf(name) in IMAGE_EXTENSIONS

    /**
     * 配对键：`父目录/去掉扩展名的文件名`。
     *
     * 为什么要带父目录：`song.lrc` 这种名字在整棵远程树里到处都是（一张专辑一个），
     * 只用文件名做键会让 A 专辑的歌配上 B 专辑的歌词。
     */
    fun sidecarKey(parentDir: String, fileName: String): String =
        parentDir + "/" + MediaScanRules.titleFromFileName(fileName)

    /** 扩展名小写；没有扩展名返回空串 */
    private fun extensionOf(name: String): String = name.substringAfterLast('.', "").lowercase()
}
