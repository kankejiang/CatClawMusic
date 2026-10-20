package com.catclaw.music.kt.core.library

/**
 * 音乐库 hub 两张卡的聚合规则：「存储占用」的分档占用、「数据洞察」的分布数据集。
 *
 * 与 MAUI 一样在**内存里**分组（C# `LibraryViewModel.ComputePieDatasets` 也是拿 localSongs 现算），
 * 不写 SQL：一次读回四列就够两张卡用，而分组规则放 `:core` 才能被单测钉住
 * （"分组键写错"这种事运行时不报错，只是数字看着不太对）。
 *
 * ⚠ 与 MAUI 的一处**结构性差异**，别再当成漏做：
 * C# 的分组键是**文件扩展名**（`GetFileExtension(s.FilePath)`），而本端本地曲的 `FilePath`
 * 存的是 MediaStore 的 `content://` URI —— URI 里既没有文件名也没有后缀（见 `MediaPathResolver`
 * 的类注释），所以"音频格式"这一组在本端**根本取不到键**。
 * 于是这里的分布一律按**比特率档位**分组：它同样能回答"无损占了多少空间"，
 * 而 Bitrate 是入库时就写好的列（`MediaStore.BITRATE`），不必为每首歌再查一次 ContentResolver
 * （几千首的库上那是好几秒）。真要按后缀分组，得在扫描入库时把后缀记下来 ——
 * 那要动 `SchemaParityTest` 钉住的表结构，属另一件事。
 */

/** 一首歌参与聚合的四列。`:data` 的投影按这几个名字取列。 */
data class SongStat(
    val filePath: String,
    val fileSize: Long,
    val bitrateKbps: Int,
    val artist: String,
)

/**
 * 音质档位。刻度与 C# `QualityBucket` 的比特率分支对齐（320 / 256 / 192 / 128），
 * 少的是"无损按后缀判断"那一条（同上：后缀取不到）。
 */
enum class QualityBucket { Unknown, Kbps320, Kbps256, Kbps192, Kbps128, Other }

fun qualityBucketOf(bitrateKbps: Int): QualityBucket = when {
    bitrateKbps <= 0 -> QualityBucket.Unknown
    bitrateKbps >= 300 -> QualityBucket.Kbps320
    bitrateKbps >= 240 -> QualityBucket.Kbps256
    bitrateKbps >= 160 -> QualityBucket.Kbps192
    bitrateKbps >= 100 -> QualityBucket.Kbps128
    else -> QualityBucket.Other
}

/** 一档占用的字节数 */
data class SizeBucket(val bucket: QualityBucket, val bytes: Long)

/**
 * 按档位的占用，**字节降序**（最大的一档在最上面，与 MAUI 的 `OrderByDescending(SizeBytes)` 一致）。
 * 同字节数时按档位枚举序定序：并列时的顺序不该随数据库回读顺序漂移。
 */
fun sizeBuckets(stats: List<SongStat>): List<SizeBucket> =
    stats.groupBy { qualityBucketOf(it.bitrateKbps) }
        .map { (bucket, rows) -> SizeBucket(bucket, rows.sumOf { it.fileSize }) }
        .filter { it.bytes > 0 }
        .sortedWith(compareByDescending<SizeBucket> { it.bytes }.thenBy { it.bucket.ordinal })

/** 一档的歌数（数据洞察环形图的一段） */
data class QualityCount(val bucket: QualityBucket, val count: Int)

/** 音质分布：**歌数**降序，同数按档位枚举序 */
fun qualityInsight(stats: List<SongStat>): List<QualityCount> =
    stats.groupBy { qualityBucketOf(it.bitrateKbps) }
        .map { (bucket, rows) -> QualityCount(bucket, rows.size) }
        .sortedWith(compareByDescending<QualityCount> { it.count }.thenBy { it.bucket.ordinal })

/** 分布里的一段：`label` 为空串表示"没有艺人信息"，由界面填那句本地化文案 */
data class CountSlice(val label: String, val count: Int)

/** 艺人分布：前 [top] 名 + 其余归入 [otherCount]（与 MAUI 的 Top5 + 「其他」同形） */
data class ArtistInsight(val top: List<CountSlice>, val otherCount: Int) {
    val total: Int get() = top.sumOf { it.count } + otherCount
}

fun artistInsight(stats: List<SongStat>, top: Int = 5): ArtistInsight {
    val groups = stats.groupingBy { it.artist.trim() }.eachCount()
        .entries
        .sortedWith(compareByDescending<Map.Entry<String, Int>> { it.value }.thenBy { it.key })
    val head = groups.take(top.coerceAtLeast(0)).map { CountSlice(it.key, it.value) }
    val rest = groups.drop(head.size).sumOf { it.value }
    return ArtistInsight(head, rest)
}
