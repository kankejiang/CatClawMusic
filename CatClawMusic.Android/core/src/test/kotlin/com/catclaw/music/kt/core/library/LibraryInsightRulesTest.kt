package com.catclaw.music.kt.core.library

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * 音乐库洞察的聚合规则。挑的全是"写歪了也不报错、只是数字不对"的点：
 * 分桶边界、并列时的顺序、以及"没有艺人信息"该不该并成一个桶。
 */
class LibraryInsightRulesTest {

    private fun stat(size: Long, kbps: Int, artist: String = "某人") =
        SongStat(filePath = "content://m/$size", fileSize = size, bitrateKbps = kbps, artist = artist)

    @Test
    fun bucketBoundariesMatchTheCsharpScale() {
        assertEquals(QualityBucket.Unknown, qualityBucketOf(0))
        assertEquals(QualityBucket.Unknown, qualityBucketOf(-1))
        assertEquals(QualityBucket.Kbps320, qualityBucketOf(320))
        assertEquals("300 是 320 档的下界（C# 也是 >=300）", QualityBucket.Kbps320, qualityBucketOf(300))
        assertEquals(QualityBucket.Kbps256, qualityBucketOf(299))
        assertEquals(QualityBucket.Kbps192, qualityBucketOf(192))
        assertEquals(QualityBucket.Kbps128, qualityBucketOf(128))
        assertEquals(QualityBucket.Other, qualityBucketOf(99))
    }

    @Test
    fun sizeBucketsAreOrderedByBytesAndSkipEmptyOnes() {
        val buckets = sizeBuckets(
            listOf(
                stat(100, 128),
                stat(900, 320),
                stat(50, 128),
                // 没有标签的 0 字节行：既不该出现，也不该把别的档挤下去
                stat(0, 0),
            ),
        )
        assertEquals(listOf(QualityBucket.Kbps320, QualityBucket.Kbps128), buckets.map { it.bucket })
        assertEquals("同一档要合并求和，而不是各出一行", 150L, buckets.first { it.bucket == QualityBucket.Kbps128 }.bytes)
        assertEquals("0 字节的档不画（一条长度为零的进度条看着像坏了）", 2, buckets.size)
    }

    @Test
    fun tiesFallBackToEnumOrderNotToInputOrder() {
        // 两档字节数相同：输入顺序反过来也必须得到同一个结果，否则每次刷新条形图都在跳
        val a = sizeBuckets(listOf(stat(100, 128), stat(100, 320)))
        val b = sizeBuckets(listOf(stat(100, 320), stat(100, 128)))
        assertEquals(a.map { it.bucket }, b.map { it.bucket })
        assertEquals(listOf(QualityBucket.Kbps320, QualityBucket.Kbps128), a.map { it.bucket })
    }

    @Test
    fun artistInsightKeepsTopFiveAndFoldsTheRest() {
        val stats = (1..7).map { i -> stat(10, 320, artist = "A$i") } + List(3) { stat(10, 320, artist = "A1") }
        val insight = artistInsight(stats)
        assertEquals(5, insight.top.size)
        assertEquals("A1", insight.top.first().label)
        assertEquals(4, insight.top.first().count)
        assertEquals("前 5 名之外的都要并进「其他」", 2, insight.otherCount)
        assertEquals("总数必须等于输入行数", stats.size, insight.total)
    }

    @Test
    fun artistInsightTreatsBlankArtistAsItsOwnBucket() {
        // 空艺人并成一段（界面填「未知艺人」），而不是被丢掉 —— 丢掉会让总数对不上
        val insight = artistInsight(listOf(stat(1, 320, artist = ""), stat(1, 320, artist = "  "), stat(1, 320, artist = "A")))
        assertEquals("空串与纯空白是同一个桶", listOf("", "A"), insight.top.map { it.label })
        assertEquals(2, insight.top.first().count)
        assertEquals(3, insight.total)
    }
}
