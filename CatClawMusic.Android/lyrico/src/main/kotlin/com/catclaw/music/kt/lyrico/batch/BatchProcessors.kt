package com.catclaw.music.kt.lyrico.batch

import com.catclaw.music.kt.core.lyrics.LyricsEncoding
import com.catclaw.music.kt.lyrico.lyrics.LyricoLyricsEncoder
import com.catclaw.music.kt.lyrico.lyrics.LyricoToLyrics
import com.catclaw.music.kt.lyrico.search.CoverSearchService
import com.catclaw.music.kt.lyrico.search.CoverWriteOutcome
import com.catclaw.music.kt.lyrico.search.CoverWriter
import com.catclaw.music.kt.lyrico.search.LyricsSearchService
import com.catclaw.music.kt.lyrico.search.LyricsTarget
import com.catclaw.music.kt.lyrico.search.LyricoSongMatcher
import com.catclaw.music.kt.lyrico.search.LyricoPluginSources
import com.catclaw.music.kt.lyrico.write.EditableTags
import com.catclaw.music.kt.lyrico.write.LyricsWriteOutcome
import com.catclaw.music.kt.lyrico.write.LyricsWriter
import com.catclaw.music.kt.lyrico.write.TagEditor
import com.catclaw.music.kt.lyrico.write.TagReadOutcome
import com.catclaw.music.kt.lyrico.write.TagSyncService
import com.catclaw.music.kt.lyrico.write.TagWriteOutcome
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * 所有批量处理器。
 *
 * 共同约定：
 *  - **不抛异常**：可预期的失败返回 [BatchItemResult.Failed]，别让一首歌打断整批
 *  - 已存在内容且未开「覆盖」时返回 Skipped（不算失败，用户看计数时不会以为坏了很多）
 *  - 远程曲目/不可写文件返回 Skipped 而非 Failed：那不是错误，是能力边界
 */
class BatchProcessors(
    private val lyricsSearch: LyricsSearchService,
    private val lyricsWriter: LyricsWriter,
    private val coverSearch: CoverSearchService,
    private val coverWriter: CoverWriter,
    private val tagEditor: TagEditor,
    private val tagSync: TagSyncService,
    private val pluginSources: LyricoPluginSources,
) {

    fun all(): List<BatchProcessor> = listOf(
        MatchLyricsProcessor(),
        MatchCoverProcessor(),
        MatchMetadataProcessor(),
        EditTagsProcessor(),
        LyricsFormatProcessor(),
        RenameFilesProcessor(),
        ReplayGainProcessor(),
    )

    // ─────────────────────────── 1. 匹配歌词 ───────────────────────────

    inner class MatchLyricsProcessor : BatchProcessor {
        override val operation = BatchOperation.MATCH_LYRICS

        override suspend fun process(item: BatchItemContext): BatchItemResult {
            val existing = tagEditor.readEmbeddedLyrics(item.filePath)
            if (!item.options.overwrite && !existing.isNullOrBlank()) {
                return BatchItemResult.Skipped("已有内嵌歌词")
            }
            val target = LyricsTarget(item.title, item.artist, item.album, item.durationMs)
            val matched = lyricsSearch.autoMatchDetailed(target)
            val candidate = matched.candidate
                ?: return BatchItemResult.Failed(
                    buildString {
                        append("没找到匹配歌词")
                        if (matched.errors.isNotEmpty()) {
                            append("（")
                            append(matched.errors.joinToString("；").take(120))
                            append("）")
                        }
                    },
                )
            val lyrics = LyricoToLyrics.convert(candidate.lyrics, fallbackTitle = item.title)
                ?: return BatchItemResult.Failed("候选无法解析成歌词")
            return when (
                val outcome = lyricsWriter.write(item.toSong(), lyrics, item.options.lyricsFormat)
            ) {
                is LyricsWriteOutcome.Success ->
                    BatchItemResult.Success("来自 ${candidate.sourceName}（${outcome.format.label}）")
                is LyricsWriteOutcome.Unsupported -> BatchItemResult.Skipped(outcome.reason)
                is LyricsWriteOutcome.Failed -> BatchItemResult.Failed(outcome.message)
            }
        }
    }

    // ─────────────────────────── 2. 匹配封面 ───────────────────────────

    inner class MatchCoverProcessor : BatchProcessor {
        override val operation = BatchOperation.MATCH_COVER

        override suspend fun process(item: BatchItemContext): BatchItemResult {
            if (!item.options.overwrite) {
                val hasCover = withContext(Dispatchers.IO) {
                    runCatching {
                        tagEditor.hasEmbeddedPicture(item.filePath)
                    }.getOrDefault(false)
                }
                if (hasCover) return BatchItemResult.Skipped("已有内嵌封面")
            }
            val target = LyricsTarget(item.title, item.artist, item.album, item.durationMs)
            val cover = coverSearch.search(target).firstOrNull()
                ?: return BatchItemResult.Failed("没找到匹配封面")
            return when (val outcome = coverWriter.write(item.toSong(), cover)) {
                is CoverWriteOutcome.Success ->
                    BatchItemResult.Success("来自 ${cover.sourceName}（${outcome.bytes / 1024} KB）")
                is CoverWriteOutcome.AppOnly ->
                    BatchItemResult.Success("来自 ${cover.sourceName}，应用内封面（${outcome.bytes / 1024} KB）")
                is CoverWriteOutcome.Unsupported -> BatchItemResult.Skipped(outcome.reason)
                is CoverWriteOutcome.Failed -> BatchItemResult.Failed(outcome.message)
            }
        }
    }

    // ──────────────────────── 3. 匹配元数据 ─────────────────────────

    inner class MatchMetadataProcessor : BatchProcessor {
        override val operation = BatchOperation.MATCH_METADATA

        override suspend fun process(item: BatchItemContext): BatchItemResult {
            val target = LyricsTarget(item.title, item.artist, item.album, item.durationMs)
            // 用插件的歌曲记录而不是只查 LRCLIB：插件带 date 等更完整字段
            val hit = pluginSources.availablePlugins()
                .firstOrNull { it.manifest.hasSearchSongs }
                ?.let { plugin ->
                    LyricoSongMatcher.pickBest(target, pluginSources.searchSongsHits(plugin.id, target))
                }
                ?: return BatchItemResult.Failed("没找到匹配的元数据")

            val read = tagEditor.read(item.filePath)
            val current = (read as? TagReadOutcome.Success)?.tags
                ?: return BatchItemResult.Failed("读取标签失败")

            val onlyFillMissing = item.options.onlyFillMissing
            fun merge(currentValue: String, candidate: String): String = when {
                candidate.isBlank() -> currentValue
                onlyFillMissing && currentValue.isNotBlank() -> currentValue
                else -> candidate
            }
            val updated = current.copy(
                title = merge(current.title, hit.title),
                artist = merge(current.artist, hit.artist),
                album = merge(current.album, hit.album),
            )
            val outcome = tagEditor.write(item.filePath, current, updated)
            return when (outcome) {
                is TagWriteOutcome.Success -> {
                    if (outcome.changed.isEmpty()) {
                        BatchItemResult.Skipped("元数据已完整")
                    } else {
                        runCatching {
                            tagSync.sync(
                                item.songId, updated, currentGenre = null,
                                currentYear = item.year, currentTrack = item.trackNumber,
                            )
                        }
                        BatchItemResult.Success("补全 ${outcome.changed.joinToString()}")
                    }
                }
                is TagWriteOutcome.Unsupported -> BatchItemResult.Skipped(outcome.reason)
                is TagWriteOutcome.Failed -> BatchItemResult.Failed(outcome.message)
            }
        }

    }


    // ─────────────────────── 4. 批量改标签 ───────────────────────

    inner class EditTagsProcessor : BatchProcessor {
        override val operation = BatchOperation.EDIT_TAGS

        override suspend fun process(item: BatchItemContext): BatchItemResult {
            val opts = item.options
            if (opts.setFields.isEmpty() && opts.clearFields.isEmpty()) {
                return BatchItemResult.Failed("没有要改的字段")
            }
            val read = tagEditor.read(item.filePath)
            val current = (read as? TagReadOutcome.Success)?.tags
                ?: return BatchItemResult.Failed("读取标签失败")

            val edited = applyEdits(current, opts, item)
            return when (val outcome = tagEditor.write(item.filePath, current, edited)) {
                is TagWriteOutcome.Success -> {
                    if (outcome.changed.isEmpty()) {
                        BatchItemResult.Skipped("无需改动")
                    } else {
                        runCatching {
                            tagSync.sync(
                                item.songId, edited, currentGenre = null,
                                currentYear = item.year, currentTrack = item.trackNumber,
                            )
                        }
                        BatchItemResult.Success("改了 ${outcome.changed.joinToString()}")
                    }
                }
                is TagWriteOutcome.Unsupported -> BatchItemResult.Skipped(outcome.reason)
                is TagWriteOutcome.Failed -> BatchItemResult.Failed(outcome.message)
            }
        }

        private fun applyEdits(tags: EditableTags, opts: BatchOptions, item: BatchItemContext): EditableTags {
            var result = tags
            opts.setFields.forEach { (key, value) ->
                // {title} {artist} 这类占位符按当前歌曲替换
                val resolved = expandPlaceholders(value, item)
                result = when (key) {
                    "TITLE", "title" -> result.copy(title = resolved)
                    "ARTIST", "artist" -> result.copy(artist = resolved)
                    "ALBUM", "album" -> result.copy(album = resolved)
                    "ALBUMARTIST", "albumArtist" -> result.copy(albumArtist = resolved)
                    "GENRE", "genre" -> result.copy(genre = resolved)
                    "DATE", "date" -> result.copy(date = resolved)
                    "TRACKNUMBER", "track" -> result.copy(trackNumber = resolved)
                    "DISCNUMBER", "disc" -> result.copy(discNumber = resolved)
                    "COMPOSER", "composer" -> result.copy(composer = resolved)
                    "LYRICIST", "lyricist" -> result.copy(lyricist = resolved)
                    "COMMENT", "comment" -> result.copy(comment = resolved)
                    else -> result
                }
            }
            opts.clearFields.forEach { key ->
                result = when (key) {
                    "TITLE", "title" -> result.copy(title = "")
                    "ARTIST", "artist" -> result.copy(artist = "")
                    "ALBUM", "album" -> result.copy(album = "")
                    "ALBUMARTIST", "albumArtist" -> result.copy(albumArtist = "")
                    "GENRE", "genre" -> result.copy(genre = "")
                    "DATE", "date" -> result.copy(date = "")
                    "TRACKNUMBER", "track" -> result.copy(trackNumber = "")
                    "DISCNUMBER", "disc" -> result.copy(discNumber = "")
                    "COMPOSER", "composer" -> result.copy(composer = "")
                    "LYRICIST", "lyricist" -> result.copy(lyricist = "")
                    "COMMENT", "comment" -> result.copy(comment = "")
                    else -> result
                }
            }
            return result
        }
    }

    // ────────────────────── 5. 歌词格式转换 ──────────────────────

    inner class LyricsFormatProcessor : BatchProcessor {
        override val operation = BatchOperation.LYRICS_FORMAT

        override suspend fun process(item: BatchItemContext): BatchItemResult {
            val raw = tagEditor.readEmbeddedLyrics(item.filePath)
            if (raw.isNullOrBlank()) return BatchItemResult.Skipped("没有内嵌歌词")
            // 用工程里的统一解析入口：内嵌歌词可能是 LRC / TTML / AMLL / 纯文本，
            // 只用 LrcParser 会把 TTML 判成「无法解析」（实测踩到）
            val parsed = LyricsEncoding.parseContent(raw)
                ?: return BatchItemResult.Failed("已有歌词无法解析")
            val encoded = LyricoLyricsEncoder.encode(parsed, item.options.lyricsFormat)
            if (encoded.isBlank()) return BatchItemResult.Failed("转换结果为空")
            if (encoded == raw) return BatchItemResult.Skipped("已是目标格式")
            val ok = tagEditor.writeRawLyrics(item.filePath, encoded)
            return if (ok) {
                BatchItemResult.Success("转为 ${item.options.lyricsFormat.label}")
            } else {
                BatchItemResult.Failed("写入失败")
            }
        }
    }

    // ──────────────────────── 6. 重命名文件 ────────────────────────

    inner class RenameFilesProcessor : BatchProcessor {
        override val operation = BatchOperation.RENAME_FILES

        override suspend fun process(item: BatchItemContext): BatchItemResult = withContext(Dispatchers.IO) {
            if (item.filePath.startsWith("content://")) {
                return@withContext BatchItemResult.Skipped("文档路径不支持重命名")
            }
            val source = File(item.filePath)
            if (!source.isFile) return@withContext BatchItemResult.Failed("文件不存在")

            val baseName = expandPlaceholders(item.options.renamePattern, item)
                .replace(Regex("""[\\/:*?"<>|]"""), "_")
                .trim()
            if (baseName.isBlank()) return@withContext BatchItemResult.Failed("模板生成空文件名")

            val ext = source.extension
            val targetName = if (ext.isBlank()) baseName else "$baseName.$ext"
            val target = File(source.parentFile, targetName)
            if (target.absolutePath == source.absolutePath) {
                return@withContext BatchItemResult.Skipped("文件名已符合模板")
            }
            if (target.exists()) {
                return@withContext BatchItemResult.Failed("目标文件已存在：${target.name}")
            }
            val moved = runCatching { source.renameTo(target) }.getOrDefault(false)
            if (!moved) return@withContext BatchItemResult.Failed("重命名失败")
            // 曲库里的路径要一起改，否则下次播放找不到文件
            runCatching { tagSync.updateFilePath(item.songId, target.absolutePath) }
            BatchItemResult.Success("→ ${target.name}")
        }
    }

    // ───────────────────────── 7. ReplayGain ─────────────────────────

    /**
     * ReplayGain 需要解码整轨道做 EBU R128 分析（Lyrico 走 ffmpeg + ebur128 子模块）。
     * 本工程尚未移植这两个原生依赖，因此**明确跳过并说明原因**，
     * 而不是假装成功或静默失败。
     */
    inner class ReplayGainProcessor : BatchProcessor {
        override val operation = BatchOperation.REPLAY_GAIN

        override suspend fun process(item: BatchItemContext): BatchItemResult =
            BatchItemResult.Skipped("未移植 ReplayGain 分析（需要 ffmpeg + ebur128）")
    }

    // ─────────────────────────── 工具 ───────────────────────────


    private fun BatchItemContext.toSong(): com.catclaw.music.kt.core.model.Song =
        com.catclaw.music.kt.core.model.Song(
            id = songId,
            title = title,
            artist = artist,
            album = album,
            durationSeconds = (durationMs / 1000).toInt(),
            filePath = filePath,
        )

    private fun expandPlaceholders(pattern: String, item: BatchItemContext): String =
        pattern
            .replace("{artist}", item.artist.ifBlank { "未知艺人" })
            .replace("{title}", item.title)
            .replace("{album}", item.album.ifBlank { "未知专辑" })
            .replace("{track}", item.trackNumber.toString().padStart(2, '0'))
            .replace("{year}", if (item.year > 0) item.year.toString() else "")
}
