package com.catclaw.music.kt.core.library

/**
 * 一条音频的标签值，字段与 Songs 表的元数据列一一对应。
 *
 * 本地 SAF 扫描、远程补全、播放时现读三个来源都归到这个形状，免得每个来源各写一份
 * 「retriever 抽了哪几个键」的私有结构。
 *
 * 时长用毫秒：`MediaMetadataRetriever` 与 MediaStore 给的都是毫秒，入库契约才是秒，
 * 换算走 [MediaScanRules.durationSeconds]。
 */
data class AudioTags(
    val title: String = "",
    val artist: String? = null,
    val album: String = "",
    val genre: String? = null,
    val durationMs: Long = 0L,
    val bitrateKbps: Int = 0,
    val trackNumber: Int = 0,
    val year: Int = 0,
)
