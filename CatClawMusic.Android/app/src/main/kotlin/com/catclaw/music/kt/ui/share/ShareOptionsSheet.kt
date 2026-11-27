package com.catclaw.music.kt.ui.share

import android.content.ContentValues
import android.content.Context
import android.graphics.BitmapFactory
import android.graphics.SurfaceTexture
import android.media.MediaPlayer
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.view.Surface
import android.view.TextureView
import android.provider.MediaStore
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.annotation.DrawableRes
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.slideInVertically
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.lyrics.LyricsReader
import com.catclaw.music.kt.ui.player.CoverTile
import com.catclaw.music.kt.ui.player.loadCoverBitmap
import com.catclaw.music.kt.ui.song.shareDir
import com.catclaw.music.kt.ui.song.shareFile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.inkOn
import com.catclaw.music.kt.ui.theme.liquidGlass
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import java.io.File
import java.io.FileOutputStream
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * 分享二选一：高光歌词图片 / 歌词视频，并让用户自己挑要分享的歌词行。
 *
 * 与长按菜单一样由 `CatClawRoot` 根层渲染 —— 只有与背景录制层同级，液态玻璃才是真玻璃。
 */

/** 分享导出要用到的两个单例；Composable 里没有注入点，用 Hilt EntryPoint 取。 */
@EntryPoint
@InstallIn(SingletonComponent::class)
internal interface ShareDeps {
    fun lyricsReader(): LyricsReader
    fun playerController(): PlayerController
}

private data class ShareTarget(val song: Song, val isCurrent: Boolean)

private enum class ShareMode { Image, Video }

private var pendingShare by mutableStateOf<ShareTarget?>(null)

/** 从歌曲菜单打开分享层：菜单自己会先 dismiss，这里只置位。 */
fun openShareOptions(song: Song, isCurrent: Boolean) {
    pendingShare = ShareTarget(song, isCurrent)
}

@Composable
fun ShareOptionsHost() {
    val target = pendingShare ?: return
    ShareOptionsSheet(target, onDismiss = { pendingShare = null })
}

private fun deps(context: Context): ShareDeps? = runCatching {
    EntryPointAccessors.fromApplication(context.applicationContext, ShareDeps::class.java)
}.getOrNull()

private fun shareText(song: Song): String =
    if (song.artist.isBlank()) song.title else "${song.title} - ${song.artist}"

@Composable
private fun ShareOptionsSheet(target: ShareTarget, onDismiss: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val main = remember { Handler(Looper.getMainLooper()) }

    // progress < 0 表示空闲；>= 0 表示正在导出（0..1）
    var progress by remember { mutableStateOf(-1f) }
    var message by remember { mutableStateOf<String?>(null) }
    var job by remember { mutableStateOf<Job?>(null) }
    var mode by remember { mutableStateOf<ShareMode?>(null) }
    var lines by remember { mutableStateOf<List<LyricLine>>(emptyList()) }
    var selection by remember { mutableStateOf<List<Int>>(emptyList()) }
    var loadingLyrics by remember { mutableStateOf(false) }
    // 视频生成完先进试看，用户确认后才调系统分享
    var previewFile by remember { mutableStateOf<File?>(null) }
    // 视频生成成功但没有音频时，记录原因展示在预览下方
    var audioNote by remember { mutableStateOf<String?>(null) }
    var resumePlayerAfterPreview by remember { mutableStateOf(false) }

    val working = progress >= 0f
    val busy = working || loadingLyrics

    fun cancelJob() {
        job?.cancel()
        job = null
        loadingLyrics = false
        progress = -1f
    }

    /** 离开试看：停掉预览、必要时恢复之前暂停的播放器。 */
    fun leavePreview(delete: Boolean) {
        if (delete) previewFile?.delete()
        previewFile = null
        audioNote = null
        if (resumePlayerAfterPreview) {
            resumePlayerAfterPreview = false
            deps(context)?.playerController()?.resume()
        }
    }

    fun enterPreview(file: File, pausePlayback: Boolean) {
        // 视频预览自带声音，先把正在播的歌暂停，离开时再恢复；图片预览不用动播放器
        if (pausePlayback) {
            val controller = deps(context)?.playerController()
            resumePlayerAfterPreview = controller?.state?.value?.isPlaying == true
            if (resumePlayerAfterPreview) controller?.pause()
        } else {
            resumePlayerAfterPreview = false
        }
        previewFile = file
    }

    fun close() {
        job?.cancel()
        job = null
        leavePreview(delete = true)
        onDismiss()
    }

    fun backToOptions() {
        if (busy) return
        mode = null
        lines = emptyList()
        selection = emptyList()
        message = null
    }

    /** 先取歌词再进选择页：取不到就留在选项页并提示。 */
    fun openPicker(targetMode: ShareMode) {
        if (busy) return
        message = null
        loadingLyrics = true
        job = scope.launch {
            try {
                val shareDeps = deps(context)
                if (shareDeps == null) {
                    message = context.getString(R.string.share_export_failed)
                    return@launch
                }
                val lyrics = runCatching { shareDeps.lyricsReader().loadFor(target.song) }.getOrNull()
                val valid = validLyricLines(lyrics)
                if (valid.isEmpty()) {
                    message = context.getString(R.string.share_no_lyrics)
                    return@launch
                }
                val position = if (target.isCurrent) shareDeps.playerController().currentPositionMs() else 0L
                val index = lyricIndexAt(valid, position).coerceAtLeast(0)
                lines = valid
                selection = when (targetMode) {
                    ShareMode.Image -> listOfNotNull(
                        index,
                        (index + 1).takeIf { it <= valid.lastIndex },
                    )
                    ShareMode.Video -> {
                        // 默认给一段连续的歌词（当前位置起约 15 秒，至少 4 行），
                        // 画面不会只有两行显得空；用户点第一行和最后一行即可改范围。
                        var end = index
                        while (end + 1 <= valid.lastIndex &&
                            valid[end + 1].startMs - valid[index].startMs < 15_000
                        ) {
                            end++
                        }
                        end = maxOf(end, (index + 3).coerceAtMost(valid.lastIndex))
                        (index..end).toList()
                    }
                }
                mode = targetMode
            } catch (c: CancellationException) {
                throw c
            } catch (t: Throwable) {
                message = context.getString(R.string.share_export_failed)
            } finally {
                loadingLyrics = false
                job = null
            }
        }
    }

    fun toggleLine(index: Int) {
        if (busy) return
        if (mode == ShareMode.Video) {
            // 视频只选「第一行 + 最后一行」，中间自动连续。
            // 选过一次后，再点任意位置会移动较近的那一端，方便微调范围。
            if (selection.isEmpty()) {
                selection = listOf(index)
            } else {
                val start = selection.first()
                val end = selection.last()
                selection = when {
                    start == end -> (minOf(start, index)..maxOf(start, index)).toList()
                    index < start -> (index..end).toList()
                    index > end -> (start..index).toList()
                    index - start <= end - index -> (index..end).toList()
                    else -> (start..index).toList()
                }
            }
            return
        }
        selection = when {
            index in selection -> selection - index
            selection.size < 2 -> selection + index
            else -> selection.drop(1) + index
        }
    }

    fun startExport() {
        val chosenMode = mode ?: return
        if (busy || selection.isEmpty() || lines.isEmpty()) return
        message = null
        progress = 0f
        audioNote = null
        job = scope.launch {
            try {
                val shareDeps = deps(context)
                if (shareDeps == null) {
                    message = context.getString(R.string.share_export_failed)
                    return@launch
                }
                when (chosenMode) {
                    ShareMode.Image -> {
                        val picked = selection.sorted()
                            .mapNotNull { lines.getOrNull(it)?.text?.trim() }
                            .filter { it.isNotEmpty() }
                        if (picked.isEmpty()) {
                            message = context.getString(R.string.share_no_lyrics)
                            return@launch
                        }
                        val cover = loadCoverBitmap(
                            context, target.song.coverArtPath, target.song.filePath, target.song.id, 1440,
                        )
                        val bitmap = ShareCardRenderer.render(
                            target.song,
                            picked,
                            cover,
                            loadBrandTypeface(context),
                        )
                        val file = File(
                            shareDir(context),
                            "catclaw-lyric-${target.song.id}-${System.currentTimeMillis()}.jpg",
                        )
                        withContext(Dispatchers.IO) {
                            FileOutputStream(file).use {
                                bitmap.compress(android.graphics.Bitmap.CompressFormat.JPEG, 95, it)
                            }
                        }
                        bitmap.recycle()
                        // 先预览，确认后再分享
                        enterPreview(file, pausePlayback = false)
                    }

                    ShareMode.Video -> {
                        val pickedIndices = selection.sorted().filter { lines.getOrNull(it) != null }
                        if (pickedIndices.isEmpty()) {
                            message = context.getString(R.string.share_no_lyrics)
                            return@launch
                        }
                        val firstStart = lines[pickedIndices.first()].startMs
                        val lastStart = lines[pickedIndices.last()].startMs
                        // 覆盖到最后一条选中行之后 + 0.8s 尾巴；行数不限，时长跟着范围走
                        val nextStart = lines.getOrNull(pickedIndices.last() + 1)?.startMs
                            ?: (lastStart + 4_000)
                        val duration = (nextStart - firstStart + 800)
                            .coerceAtLeast(LyricVideoExporter.MIN_DURATION_MS)
                        // 中间的歌词全部进画面
                        val display = pickedIndices.mapNotNull { lines.getOrNull(it) }
                        // 在线曲：先解析一条新鲜直链；下载 / 鉴权都走播放器同一套 DataSource
                        val audioSong = runCatching {
                            val url = shareDeps.playerController().audioUrlForExport(target.song)
                            if (url.isNotBlank()) target.song.copy(filePath = url) else target.song
                        }.getOrDefault(target.song)
                        val file = File(
                            shareDir(context),
                            "catclaw-lyric-${target.song.id}-${System.currentTimeMillis()}.mp4",
                        )
                        val result = LyricVideoExporter.export(
                            context, audioSong, display, lines, firstStart, duration, file,
                        ) { p ->
                            // 导出跑在 Default / IO 线程，进度回主线程再写 Compose 状态；
                            // 取消后（progress = -1）迟到的回调不再把面板拉回「导出中」
                            main.post { if (progress >= 0f) progress = p.coerceIn(0f, 1f) }
                        }
                        if (!result.success) {
                            message = context.getString(R.string.share_export_failed)
                            return@launch
                        }
                        audioNote = result.audioNote
                        // 生成成功先进试看，用户确认后才调系统分享
                        enterPreview(file, pausePlayback = true)
                    }
                }
            } catch (c: CancellationException) {
                throw c
            } catch (t: Throwable) {
                message = context.getString(R.string.share_export_failed)
            } finally {
                progress = -1f
                job = null
            }
        }
    }

    BackHandler {
        when {
            previewFile != null -> leavePreview(delete = true)
            working -> cancelJob()
            mode != null && !loadingLyrics -> backToOptions()
            else -> close()
        }
    }

    Box(Modifier.fillMaxSize()) {
        var entered by remember { mutableStateOf(false) }
        LaunchedEffect(Unit) { entered = true }

        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.42f))
                .clickable(
                    interactionSource = remember { MutableInteractionSource() },
                    indication = null,
                ) { if (working) cancelJob() else close() },
        )

        AnimatedVisibility(
            visible = entered,
            enter = slideInVertically(tween(180)) { it / 4 } + fadeIn(tween(140)),
            modifier = Modifier.align(Alignment.BottomCenter),
        ) {
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .navigationBarsPadding()
                    .padding(horizontal = 10.dp, vertical = 12.dp)
                    .liquidGlass(palette = palette, corner = 28)
                    .clickable(
                        interactionSource = remember { MutableInteractionSource() },
                        indication = null,
                    ) {}
                    .padding(top = 8.dp, bottom = 8.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Grabber()
                val currentMode = mode
                if (currentMode == null || working || previewFile != null) {
                    ShareHeader(target.song)
                } else {
                    PickerHeader(
                        mode = currentMode,
                        songLabel = shareText(target.song),
                        onBack = { backToOptions() },
                    )
                }

                when {
                    working -> ExportProgress(progress)
                    loadingLyrics -> LoadingRow()
                    previewFile != null -> {
                        message?.let { NoticeRow(it) }
                        PreviewBody(
                            file = previewFile!!,
                            isVideo = currentMode != ShareMode.Image,
                            audioNote = audioNote,
                        )
                    }
                    currentMode == null -> {
                        message?.let { NoticeRow(it) }
                        OptionRow(
                            icon = R.drawable.ic_lyrics,
                            title = stringResource(R.string.share_image_title),
                            subtitle = stringResource(R.string.share_image_subtitle),
                            onClick = { openPicker(ShareMode.Image) },
                        )
                        Spacer(Modifier.height(2.dp))
                        OptionRow(
                            icon = R.drawable.ic_screen_rotation,
                            title = stringResource(R.string.share_video_title),
                            subtitle = stringResource(R.string.share_video_subtitle),
                            onClick = { openPicker(ShareMode.Video) },
                        )
                    }
                    else -> {
                        message?.let { NoticeRow(it) }
                        LyricPicker(lines = lines, selection = selection, onToggle = { toggleLine(it) })
                    }
                }

                Spacer(Modifier.height(10.dp))
                val preview = previewFile
                when {
                    preview != null -> {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp),
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            PillButton(
                                text = stringResource(R.string.share_preview_reselect),
                                filled = false,
                                modifier = Modifier.weight(1f),
                                onClick = { leavePreview(delete = true) },
                            )
                            PillButton(
                                text = stringResource(R.string.share_save),
                                filled = false,
                                modifier = Modifier.weight(1f),
                                onClick = {
                                    val video = mode != ShareMode.Image
                                    scope.launch {
                                        val ok = withContext(Dispatchers.IO) {
                                            saveToGallery(context, preview, video)
                                        }
                                        if (ok) {
                                            Toast.makeText(
                                                context,
                                                R.string.share_saved,
                                                Toast.LENGTH_SHORT,
                                            ).show()
                                        } else {
                                            message = context.getString(R.string.share_save_failed)
                                        }
                                    }
                                },
                            )
                            PillButton(
                                text = stringResource(R.string.share_confirm_share),
                                filled = true,
                                modifier = Modifier.weight(1f),
                                onClick = {
                                    val mime = if (mode == ShareMode.Image) "image/jpeg" else "video/mp4"
                                    val ok = shareFile(
                                        context, preview, mime, shareText(target.song),
                                        context.getString(R.string.share_chooser_title),
                                    )
                                    if (ok) {
                                        leavePreview(delete = false)
                                        onDismiss()
                                    } else {
                                        message = context.getString(R.string.share_export_failed)
                                    }
                                },
                            )
                        }
                    }
                    working || currentMode == null -> {
                        PillButton(
                            text = stringResource(
                                if (working) R.string.share_cancel_export else R.string.share_cancel,
                            ),
                            filled = false,
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp),
                            onClick = { if (working) cancelJob() else close() },
                        )
                    }
                    else -> {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp),
                            horizontalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            PillButton(
                                text = stringResource(R.string.share_cancel),
                                filled = false,
                                modifier = Modifier.weight(1f),
                                onClick = { backToOptions() },
                            )
                            PillButton(
                                text = stringResource(
                                    if (currentMode == ShareMode.Image) {
                                        R.string.share_generate_image
                                    } else {
                                        R.string.share_generate_video
                                    },
                                ),
                                filled = true,
                                modifier = Modifier.weight(1f),
                                onClick = { startExport() },
                            )
                        }
                    }
                }
                Spacer(Modifier.height(4.dp))
            }
        }
    }
}

@Composable
private fun Grabber() {
    val palette = LocalCatClawPalette.current
    Box(
        modifier = Modifier
            .padding(top = 4.dp, bottom = 10.dp)
            .width(38.dp)
            .height(4.dp)
            .clip(CircleShape)
            .background(Color(palette.textSecondary).copy(alpha = 0.35f)),
    )
}

@Composable
private fun ShareHeader(song: Song) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 18.dp, end = 18.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        CoverTile(
            coverArtPath = song.coverArtPath,
            audioPath = song.filePath,
            songId = song.id,
            maxPx = 128,
            modifier = Modifier
                .size(44.dp)
                .clip(RoundedCornerShape(12.dp)),
        ) {
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(Color(palette.primary).copy(alpha = 0.16f)),
                contentAlignment = Alignment.Center,
            ) {
                Text(
                    text = song.title.take(1),
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
            }
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = stringResource(R.string.share_title),
                fontSize = 17.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
            )
            Text(
                text = shareText(song),
                fontSize = 12.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun PickerHeader(mode: ShareMode, songLabel: String, onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = 10.dp, end = 16.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Box(
            modifier = Modifier
                .size(34.dp)
                .clip(CircleShape)
                .clickable(onClick = onBack),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_back),
                contentDescription = stringResource(R.string.share_back),
                tint = Color(palette.textSecondary),
                modifier = Modifier.size(17.dp),
            )
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = stringResource(R.string.share_pick_lyrics),
                fontSize = 16.5.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
            )
            val hint = stringResource(
                if (mode == ShareMode.Image) R.string.share_pick_hint_image else R.string.share_pick_hint_video,
            )
            Text(
                text = "$songLabel · $hint",
                fontSize = 11.5.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun OptionRow(
    @DrawableRes icon: Int,
    title: String,
    subtitle: String,
    onClick: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(13.dp),
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(RoundedCornerShape(12.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.07f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(icon),
                contentDescription = null,
                tint = Color(palette.textPrimary).copy(alpha = 0.82f),
                modifier = Modifier.size(20.dp),
            )
        }
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = title,
                fontSize = 15.sp,
                fontWeight = FontWeight.Medium,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = subtitle,
                fontSize = 11.5.sp,
                color = Color(palette.textSecondary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Icon(
            painter = painterResource(R.drawable.ic_arrow_forward),
            contentDescription = null,
            tint = Color(palette.textPrimary).copy(alpha = 0.4f),
            modifier = Modifier.size(15.dp),
        )
    }
}

@Composable
private fun LyricPicker(lines: List<LyricLine>, selection: List<Int>, onToggle: (Int) -> Unit) {
    val palette = LocalCatClawPalette.current
    LazyColumn(
        modifier = Modifier
            .fillMaxWidth()
            .heightIn(max = 300.dp),
        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 2.dp),
    ) {
        itemsIndexed(lines) { index, line ->
            val selected = index in selection
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 1.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(
                        if (selected) Color(palette.primary).copy(alpha = 0.16f) else Color.Transparent,
                    )
                    .clickable { onToggle(index) }
                    .padding(horizontal = 12.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Box(
                    modifier = Modifier
                        .size(6.dp)
                        .clip(CircleShape)
                        .background(
                            if (selected) Color(palette.primary)
                            else Color(palette.textSecondary).copy(alpha = 0.35f),
                        ),
                )
                Text(
                    text = line.text.trim(),
                    fontSize = 14.sp,
                    fontWeight = if (selected) FontWeight.Medium else FontWeight.Normal,
                    color = if (selected) Color(palette.textPrimary) else Color(palette.textSecondary),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
            }
        }
    }
}

@Composable
private fun NoticeRow(text: String) {
    val palette = LocalCatClawPalette.current
    Text(
        text = text,
        fontSize = 12.5.sp,
        color = Color(palette.textPrimary).copy(alpha = 0.78f),
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 6.dp),
    )
}

@Composable
private fun LoadingRow() {
    val palette = LocalCatClawPalette.current
    Text(
        text = stringResource(R.string.share_loading_lyrics),
        fontSize = 13.sp,
        color = Color(palette.textSecondary),
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 18.dp),
    )
}

@Composable
private fun ExportProgress(progress: Float) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 18.dp, vertical = 8.dp),
    ) {
        Text(
            text = stringResource(R.string.share_exporting),
            fontSize = 14.sp,
            color = Color(palette.textPrimary),
        )
        Spacer(Modifier.height(10.dp))
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .height(6.dp)
                .clip(CircleShape)
                .background(Color(palette.textPrimary).copy(alpha = 0.12f)),
        ) {
            Box(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(progress.coerceIn(0f, 1f))
                    .clip(CircleShape)
                    .background(Color(palette.primary)),
            )
        }
        Spacer(Modifier.height(6.dp))
        Text(
            text = "${(progress.coerceIn(0f, 1f) * 100).toInt()}%",
            fontSize = 12.sp,
            color = Color(palette.textSecondary),
        )
    }
}

@Composable
private fun PreviewBody(file: File, isVideo: Boolean, audioNote: String? = null) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        if (isVideo) {
            VideoPreview(
                file = file,
                modifier = Modifier
                    .height(300.dp)
                    .aspectRatio(9f / 16f)
                    .clip(RoundedCornerShape(18.dp))
                    .background(Color.Black),
            )
        } else {
            ImagePreview(file)
        }
        Spacer(Modifier.height(8.dp))
        Text(
            text = stringResource(
                if (isVideo) R.string.share_preview_hint else R.string.share_preview_image_hint,
            ),
            fontSize = 11.5.sp,
            color = Color(palette.textSecondary),
        )
        if (isVideo && audioNote != null) {
            Text(
                text = stringResource(R.string.share_preview_no_audio, audioNote),
                fontSize = 11.5.sp,
                color = Color(0xFFE57373),
            )
        }
    }
}

/** 图片预览：直接解码生成的 JPEG（1080×1240），按卡片比例展示 */
@Composable
private fun ImagePreview(file: File) {
    val bitmap by produceState<ImageBitmap?>(initialValue = null, key1 = file) {
        value = withContext(Dispatchers.IO) {
            BitmapFactory.decodeFile(file.absolutePath)?.asImageBitmap()
        }
    }
    Box(
        modifier = Modifier
            .height(340.dp)
            .aspectRatio(1080f / 1240f)
            .clip(RoundedCornerShape(18.dp))
            .background(Color.Black),
        contentAlignment = Alignment.Center,
    ) {
        val image = bitmap
        if (image != null) {
            Image(
                bitmap = image,
                contentDescription = null,
                modifier = Modifier.fillMaxSize(),
            )
        }
    }
}

/**
 * 试看用 TextureView + MediaPlayer：VideoView 走 SurfaceView，会顶到 Compose 圆角之外，
 * 而 TextureView 是普通 View，能被 clip 正常裁切。
 */
@Composable
private fun VideoPreview(file: File, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val holder = remember(file) { VideoPlayerHolder() }
    val textureView = remember(file) {
        TextureView(context).apply {
            surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                override fun onSurfaceTextureAvailable(
                    surface: SurfaceTexture,
                    width: Int,
                    height: Int,
                ) {
                    holder.start(file, surface)
                }

                override fun onSurfaceTextureSizeChanged(
                    surface: SurfaceTexture,
                    width: Int,
                    height: Int,
                ) = Unit

                override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean {
                    holder.release()
                    return true
                }

                override fun onSurfaceTextureUpdated(surface: SurfaceTexture) = Unit
            }
            if (isAvailable) surfaceTexture?.let { holder.start(file, it) }
        }
    }
    DisposableEffect(file) {
        onDispose { holder.release() }
    }
    AndroidView(factory = { textureView }, modifier = modifier)
}

/** MediaPlayer 不进 Compose 状态，回调不应该触发重组。 */
private class VideoPlayerHolder {
    private var player: MediaPlayer? = null
    private var surface: Surface? = null
    @Volatile private var released = false

    fun start(file: File, surfaceTexture: SurfaceTexture) {
        release()
        released = false
        val mp = MediaPlayer()
        try {
            mp.setDataSource(file.absolutePath)
            val s = Surface(surfaceTexture)
            surface = s
            mp.setSurface(s)
            mp.isLooping = true
            // 预览可能被很快关掉：释放后才准备好的话不要再 start
            mp.setOnPreparedListener { if (!released) runCatching { it.start() } }
            mp.prepareAsync()
            player = mp
        } catch (t: Throwable) {
            runCatching { mp.release() }
            surface?.release()
            surface = null
        }
    }

    fun release() {
        released = true
        player?.let { mp ->
            runCatching { if (mp.isPlaying) mp.stop() }
            runCatching { mp.release() }
        }
        player = null
        surface?.release()
        surface = null
    }
}

@Composable
private fun PillButton(
    text: String,
    filled: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val palette = LocalCatClawPalette.current
    val background = if (filled) Color(palette.primary) else Color(palette.textPrimary).copy(alpha = 0.08f)
    val foreground = if (filled) Color(inkOn(palette.primary)) else Color(palette.textPrimary)
    Box(
        modifier = modifier
            .height(46.dp)
            .clip(RoundedCornerShape(16.dp))
            .background(background)
            .clickable(onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = text,
            fontSize = 14.sp,
            fontWeight = FontWeight.Medium,
            color = foreground,
        )
    }
}

/** 把预览文件写入系统相册（Android 10+ 走 MediaStore，不需要存储权限）。 */
private fun saveToGallery(context: Context, file: File, isVideo: Boolean): Boolean = runCatching {
    val resolver = context.contentResolver
    val mime = if (isVideo) "video/mp4" else "image/jpeg"
    val ext = if (isVideo) "mp4" else "jpg"
    val collection = if (isVideo) {
        MediaStore.Video.Media.EXTERNAL_CONTENT_URI
    } else {
        MediaStore.Images.Media.EXTERNAL_CONTENT_URI
    }
    val values = ContentValues().apply {
        put(MediaStore.MediaColumns.DISPLAY_NAME, "CatClaw_${System.currentTimeMillis()}.$ext")
        put(MediaStore.MediaColumns.MIME_TYPE, mime)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            put(
                MediaStore.MediaColumns.RELATIVE_PATH,
                if (isVideo) "Movies/CatClaw" else "Pictures/CatClaw",
            )
            put(MediaStore.MediaColumns.IS_PENDING, 1)
        }
    }
    val uri = resolver.insert(collection, values) ?: return@runCatching false
    resolver.openOutputStream(uri)?.use { out ->
        file.inputStream().use { it.copyTo(out) }
    } ?: return@runCatching false
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
        resolver.update(
            uri,
            ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) },
            null,
            null,
        )
    }
    true
}.getOrDefault(false)
