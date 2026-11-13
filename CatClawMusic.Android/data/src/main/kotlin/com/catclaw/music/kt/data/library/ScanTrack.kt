package com.catclaw.music.kt.data.library

/**
 * 一次扫描看到的一首音频（MediaStore 与自选文件夹两条路都收敛到这里）。
 *
 * `path` 就是入库的 FilePath，也是**去重与裁剪的唯一 key**：
 * MediaStore 那侧是 `content://media/.../<id>`，自选文件夹那侧是真实绝对路径
 * `/storage/emulated/0/...`。两者形状不同但都稳定，所以同一首歌从两条路各进来会是两行
 * —— 与 C# 侧同口径（那边也按路径去重，不做跨来源合并）。
 */
data class ScanTrack(
    val path: String,
    val mediaStoreId: Long,
    val title: String,
    val artistRaw: String?,
    val album: String,
    val durationMs: Long,
    val size: Long,
    val bitrateKbps: Int,
    val trackNumber: Int,
    val year: Int,
    val genre: String?,
    val dateModified: Long,
)
