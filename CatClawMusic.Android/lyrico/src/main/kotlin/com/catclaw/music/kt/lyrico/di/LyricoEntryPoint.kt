package com.catclaw.music.kt.lyrico.di

import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.lyrico.search.LyricsSearchService
import com.catclaw.music.kt.lyrico.write.LyricsWriter
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

/**
 * 供「非注入入口」（如临时验收探针、将来的 Intent 触发任务）取依赖用。
 * 正式功能应走 `@Inject` 构造注入，不要依赖这个入口。
 */
@EntryPoint
@InstallIn(SingletonComponent::class)
interface LyricoEntryPoint {
    fun searchService(): LyricsSearchService
    fun lyricsWriter(): LyricsWriter
    fun lyricsReader(): LyricsReader
    fun tagEditor(): com.catclaw.music.kt.lyrico.write.TagEditor
}
/** 封面搜索的注入入口（临时探针 / 将来的封面匹配页共用）。 */
@dagger.hilt.EntryPoint
@dagger.hilt.InstallIn(dagger.hilt.components.SingletonComponent::class)
interface LyricoCoverEntryPoint {
    fun coverSearchService(): com.catclaw.music.kt.lyrico.search.CoverSearchService
    fun coverWriter(): com.catclaw.music.kt.lyrico.search.CoverWriter
}