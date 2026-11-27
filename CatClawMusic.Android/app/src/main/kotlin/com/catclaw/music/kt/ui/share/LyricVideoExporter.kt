package com.catclaw.music.kt.ui.share

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BlurMaskFilter
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.media.AudioFormat
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaExtractor
import android.media.MediaFormat
import android.media.MediaMuxer
import android.net.Uri
import android.opengl.EGL14
import android.opengl.EGLConfig
import android.opengl.EGLContext
import android.opengl.EGLDisplay
import android.opengl.EGLExt
import android.opengl.EGLSurface
import android.opengl.GLES20
import android.opengl.GLUtils
import android.text.StaticLayout
import android.text.TextUtils
import android.util.Log
import android.view.Surface
import androidx.media3.common.Format
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.decoder.DecoderInputBuffer
import com.catclaw.music.kt.core.lyrics.LyricFillCalculator
import com.catclaw.music.kt.core.lyrics.LyricLine
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.RemoteAuthDataSources
import com.catclaw.music.kt.ui.player.loadCoverBitmap
import io.github.anilbeesetti.nextlib.media3ext.ffdecoder.FfmpegFlacDecoder
import io.github.anilbeesetti.nextlib.media3ext.ffdecoder.FfmpegLibrary
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import kotlin.math.max
import kotlin.math.min
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext

/**
 * 「分享歌词视频」：1080 × 1920 / 30fps / 5–30 秒 / H.264。
 *
 * 画面版式照 `design/share/lyric-video.html`。静态层（背景、曲目信息、品牌、二维码）
 * 只渲染一次，逐帧只画歌词与进度条，避免每帧重算模糊背景。
 *
 * 两条关键实现参考 google/grafika：
 * - **画面走编码器 input Surface + EGL**（`EncodeAndMuxTest` 同款），不再手写 YUV 平面，
 *   从根上避免色偏 / 绿屏；
 * - **音频统一解码成 PCM 再编码 AAC**，不再直接拷贝压缩轨 ——
 *   FLAC / Opus 等 MP4 不接受的编码也能出声。
 * 远程曲先经与播放同一套 DataSource 落到本地；任一步失败只回退静音，不让整个分享失败。
 */
object LyricVideoExporter {

    /** [audioNote] 非空表示视频生成成功但没有音频，值是失败原因；[audioDebug] 是音频参数快照。 */
    data class VideoExportResult(
        val success: Boolean,
        val audioNote: String?,
        val audioDebug: String? = null,
    )

    const val WIDTH = VIDEO_W
    const val HEIGHT = VIDEO_H
    const val FPS = 30
    const val MIN_DURATION_MS = 5_000L

    private const val TAG = "ShareExport"
    private const val LYRIC_SIZE = 69.1f
    private const val LYRIC_X = 79.9f
    private const val LYRIC_WIDTH = 920
    private const val LINE_GAP = 26f
    private const val VIEWPORT_TOP = 230f
    private const val VIEWPORT_HEIGHT = 1420f
    private const val PROGRESS_Y = 1691f
    private const val PROGRESS_H = 5.4f
    private const val SIDE_PAD = 79.9f

    /**
     * @param lines 要展示的已选歌词行（按时间排序）。
     * @param timeline 完整有效歌词，用来算当前行的卡拉OK填充（选中行之间的空档不会被拉长）。
     * @param startMs 期望的起点；实际会吸附到该位置所在行的起点。
     * @param durationMs 成片时长（下限 [MIN_DURATION_MS]，上限不设，跟随所选歌词）。
     * @return false 表示没有可用歌词或编码失败（调用方负责提示）。
     */
    suspend fun export(
        context: Context,
        song: Song,
        lines: List<LyricLine>,
        timeline: List<LyricLine>,
        startMs: Long,
        durationMs: Long,
        out: File,
        onProgress: (Float) -> Unit,
    ): VideoExportResult = withContext(Dispatchers.Default) {
        if (lines.isEmpty()) return@withContext VideoExportResult(false, "没有可用歌词")

        val start = resolveStart(lines, startMs)
        val duration = durationMs.coerceAtLeast(MIN_DURATION_MS)
        val layouts = timeline.map {
            layoutOf(
                it.text.trim(), LYRIC_SIZE, 750, CREAM, LYRIC_WIDTH,
                lineSpacingMult = 1.42f, letterSpacing = 0.015f,
            )
        }
        val totalFrames = (duration * FPS / 1000).toInt()
        onProgress(0f)

        var codec: MediaCodec? = null
        var muxer: MediaMuxer? = null
        var renderer: FrameSurfaceRenderer? = null
        var success = false
        var audioNote: String? = null
        var audioDebugText: String? = null

        try {
            out.parentFile?.mkdirs()
            if (out.exists()) out.delete()
            val audioFailures = mutableListOf<String>()
            val audioDebug = mutableListOf<String>()

            // 远程 / 在线曲没有本地文件：先用与播放同一套 DataSource（鉴权 + TLS + 缓存）
            // 把音频落到缓存，再交给 MediaExtractor。本地曲跳过这一步。
            val localAudio = if (song.filePath.startsWith("http", ignoreCase = true)) {
                // 下载进度映射到整体进度的前 20%
                downloadRemoteAudio(context, song) { p -> onProgress(p * 0.2f) }
            } else {
                null
            }
            if (song.filePath.startsWith("http", ignoreCase = true) && localAudio == null) {
                audioFailures.add("在线音频下载失败")
            }
            onProgress(0.2f)

            // 先把音频转成 AAC 并缓冲在内存里：MediaMuxer 要求所有轨道都在 start() 前 addTrack
            val audio = transcodeAudio(
                context, song, localAudio,
                start * 1000,
                (start + duration) * 1000,
                audioFailures,
                audioDebug,
            )
            audioNote = if (audio == null) audioFailures.firstOrNull() else null
            audioDebugText = if (audio != null) audioDebug.joinToString(" · ") else null
            onProgress(0.3f)

            // 封面按高分辨率取，模糊只做 2× 降采样（demo 是 blur(2px)），
            // 之前 18× 缩略图拉伸才显得满屏马赛克
            val cover = loadCoverBitmap(context, song.coverArtPath, song.filePath, song.id, 1440)
            val staticFrame = buildStaticFrame(song, cover, loadBrandTypeface(context))
            val frame = Bitmap.createBitmap(WIDTH, HEIGHT, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(frame)

            val localMuxer = MediaMuxer(out.absolutePath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4)
            muxer = localMuxer
            val mux = MuxState(localMuxer)
            if (audio != null) {
                mux.audioTrack = runCatching { localMuxer.addTrack(audio.format) }.getOrDefault(-1)
            }

            // H.264 编码器 + 输入 Surface：每帧用 GL 把 Bitmap 画到 Surface 上
            val encoder = createSurfaceEncoder()
            codec = encoder.codec
            val videoCodec = encoder.codec
            val gl = FrameSurfaceRenderer(encoder.inputSurface)
            renderer = gl
            gl.setup()

            val info = MediaCodec.BufferInfo()
            for (i in 0 until totalFrames) {
                ensureActive()
                val tMs = start + i * 1000L / FPS
                drawFrame(canvas, staticFrame, layouts, timeline, tMs, start, duration)
                gl.drawFrame(frame, i * 1_000_000L / FPS)
                drain(videoCodec, mux, info, eos = false)
                if (i % 15 == 0 || i == totalFrames - 1) {
                    onProgress(0.3f + (i + 1f) / totalFrames * 0.65f)
                }
            }
            videoCodec.signalEndOfInputStream()
            drain(videoCodec, mux, info, eos = true)

            gl.release()
            renderer = null
            runCatching { videoCodec.stop() }
            runCatching { videoCodec.release() }
            codec = null

            // 视频写完后补音频样本（时间戳从 0 开始，和画面同步）
            if (audio != null && mux.audioTrack >= 0 && mux.started) {
                writeAudioFrames(localMuxer, mux.audioTrack, audio) { p ->
                    onProgress(0.95f + p * 0.05f)
                }
            }

            if (!mux.started) error("muxer never started")
            localMuxer.stop()
            localMuxer.release()
            muxer = null
            onProgress(1f)
            success = out.isFile && out.length() > 0
        } catch (t: Throwable) {
            if (t is CancellationException) throw t
            success = false
        } finally {
            renderer?.release()
            runCatching { codec?.stop() }
            runCatching { codec?.release() }
            runCatching { muxer?.release() }
            if (!success) out.delete()
        }
        VideoExportResult(success, if (success) audioNote else null, if (success) audioDebugText else null)
    }

    // ── 时间轴 ────────────────────────────────────────────────────────────────

    private fun resolveStart(lines: List<LyricLine>, requestedMs: Long): Long {
        val first = lines.first().startMs.coerceAtLeast(0)
        if (requestedMs <= first) return first
        var hit = first
        for (line in lines) {
            if (line.startMs <= requestedMs) hit = line.startMs.coerceAtLeast(0) else break
        }
        return hit
    }

    private fun activeIndex(lines: List<LyricLine>, tMs: Long): Int {
        var index = 0
        for (i in lines.indices) {
            if (lines[i].startMs <= tMs) index = i else break
        }
        return index
    }

    // ── 逐帧绘制 ──────────────────────────────────────────────────────────────

    private fun drawFrame(
        canvas: Canvas,
        staticFrame: Bitmap,
        layouts: List<StaticLayout>,
        timeline: List<LyricLine>,
        tMs: Long,
        startMs: Long,
        durationMs: Long,
    ) {
        canvas.drawBitmap(staticFrame, 0f, 0f, null)

        val centerY = VIEWPORT_TOP + VIEWPORT_HEIGHT / 2f
        val active = activeIndex(timeline, tMs)
        val activeLayout = layouts[active]
        val slotTop = centerY - (activeLayout.height + LINE_GAP) / 2f

        // 换行时整块歌词向上滚动：480ms 三次缓出。第一行不做位移动画。
        val raw = if (active <= 0) {
            1f
        } else {
            ((tMs - timeline[active].startMs).toFloat() / 480f).coerceIn(0f, 1f)
        }
        val ease = 1f - (1f - raw) * (1f - raw) * (1f - raw)
        val scroll = if (active > 0) {
            (layouts[active - 1].height + LINE_GAP) * (1f - ease)
        } else {
            0f
        }
        val activeTop = slotTop + scroll

        canvas.save()
        canvas.clipRect(0f, VIEWPORT_TOP, WIDTH.toFloat(), VIEWPORT_TOP + VIEWPORT_HEIGHT)

        // 先铺非当前行（上下各六行），铺满整屏；越远越模糊，与全屏歌词页一致。
        var cursor = activeTop
        for (i in active - 1 downTo max(0, active - 6)) {
            cursor -= layouts[i].height + LINE_GAP
            drawDimLine(canvas, layouts[i], cursor, active - i)
        }
        cursor = activeTop + activeLayout.height + LINE_GAP
        for (i in active + 1..min(layouts.lastIndex, active + 6)) {
            drawDimLine(canvas, layouts[i], cursor, i - active)
            cursor += layouts[i].height + LINE_GAP
        }

        // 当前行：先铺一层 34% 的米白
        canvas.save()
        canvas.translate(LYRIC_X, activeTop)
        activeLayout.paint.color = CREAM_FAINT
        activeLayout.draw(canvas)
        canvas.restore()

        // 当前行的卡拉 OK 填充：进度按**完整时间轴**算，
        // 否则选中行之间的空档会被当成一句唱不完的长句，填充被拉慢。
        val current = timeline[active]
        val timelineIndex = active
        val fill = (
            if (timelineIndex >= 0) {
                LyricFillCalculator.computeFillProgress(current, timelineIndex, timeline, tMs, lineMode = false)
            } else {
                1.0
            }
            ).toFloat().coerceIn(0f, 1f)
        // 长歌词会折成多行：按「视觉行」分别推进填充，先唱完第一行再染第二行，
        // 不能整块横向一刀切（那样折行会两行同时变色）。
        if (fill > 0f) {
            val totalChars = current.text.length.coerceAtLeast(1)
            val filledChars = fill * totalChars
            for (row in 0 until activeLayout.lineCount) {
                val startChar = activeLayout.getLineStart(row)
                val endChar = activeLayout.getLineEnd(row)
                val rowChars = (endChar - startChar).coerceAtLeast(1)
                val rowFill = ((filledChars - startChar) / rowChars).coerceIn(0f, 1f)
                if (rowFill <= 0f) continue
                canvas.save()
                canvas.clipRect(
                    LYRIC_X,
                    activeTop + activeLayout.getLineTop(row) - 8f,
                    LYRIC_X + activeLayout.getLineRight(row) * rowFill,
                    activeTop + activeLayout.getLineBottom(row) + 8f,
                )
                canvas.translate(LYRIC_X, activeTop)
                activeLayout.paint.color = CREAM
                activeLayout.draw(canvas)
                canvas.restore()
            }
        }

        canvas.restore()

        // 底部细进度条
        val progress = ((tMs - startMs).toFloat() / durationMs).coerceIn(0f, 1f)
        val left = SIDE_PAD
        val right = WIDTH - SIDE_PAD
        val fillWidth = (right - left) * progress
        if (fillWidth > 0f) {
            canvas.drawRoundRect(
                RectF(left, PROGRESS_Y, left + fillWidth, PROGRESS_Y + PROGRESS_H),
                PROGRESS_H / 2f,
                PROGRESS_H / 2f,
                Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    shader = LinearGradient(
                        left, 0f, right, 0f,
                        intArrayOf(CREAM, 0xFFFFD9A8.toInt()),
                        null,
                        Shader.TileMode.CLAMP,
                    )
                },
            )
        }
    }

    private fun drawDimLine(canvas: Canvas, layout: StaticLayout, top: Float, distance: Int) {
        // 离当前行越远，字号透明度越低、模糊半径越大
        val blur = when (distance) {
            1 -> 7f
            2 -> 16f
            3 -> 26f
            else -> 36f
        }
        val alpha = when (distance) {
            1 -> 0x7A
            2 -> 0x50
            3 -> 0x38
            else -> 0x24
        }
        canvas.save()
        canvas.translate(LYRIC_X, top)
        layout.paint.color = withAlpha(CREAM, alpha)
        layout.paint.maskFilter = BlurMaskFilter(blur, BlurMaskFilter.Blur.NORMAL)
        layout.draw(canvas)
        layout.paint.maskFilter = null
        canvas.restore()
    }

    // ── 静态层 ────────────────────────────────────────────────────────────────

    private fun buildStaticFrame(
        song: Song,
        cover: Bitmap?,
        brandTypeface: Typeface?,
    ): Bitmap {
        val bitmap = Bitmap.createBitmap(WIDTH, HEIGHT, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val bounds = RectF(0f, 0f, WIDTH.toFloat(), HEIGHT.toFloat())

        if (cover != null) {
            val bg = centerCropBlur(cover, WIDTH, HEIGHT, downscale = 2, brightness = 0.62f, saturation = 1.05f)
            canvas.drawBitmap(bg, 0f, 0f, null)
            bg.recycle()
        } else {
            canvas.drawRect(bounds, Paint().apply {
                shader = LinearGradient(
                    0f, 0f, 0f, HEIGHT.toFloat(),
                    intArrayOf(0xFF2A1E15.toInt(), 0xFF140F0B.toInt()),
                    null,
                    Shader.TileMode.CLAMP,
                )
            })
        }

        canvas.drawRect(bounds, Paint().apply {
            shader = LinearGradient(
                0f, 0f, 0f, HEIGHT.toFloat(),
                intArrayOf(0xB8100B08.toInt(), 0x2E100B08, 0x4D100B08, 0xE00C0806.toInt()),
                floatArrayOf(0f, 0.30f, 0.62f, 1f),
                Shader.TileMode.CLAMP,
            )
        })
        canvas.drawRect(bounds, Paint().apply {
            shader = RadialGradient(
                WIDTH / 2f, HEIGHT * 0.46f, 900f,
                intArrayOf(0x1AFFD6A0, 0x00FFD6A0),
                floatArrayOf(0f, 0.7f),
                Shader.TileMode.CLAMP,
            )
        })

        drawGrain(canvas, bounds, alpha = 15)
        drawVignette(canvas, bounds, centerYFraction = 0.40f, inner = 0.45f, outerColor = 0x8C080503.toInt())

        // 顶部曲目信息
        val coverRect = RectF(SIDE_PAD, 77.8f, SIDE_PAD + 103.7f, 77.8f + 103.7f)
        if (cover != null) {
            drawCenterCropRound(canvas, cover, coverRect, 14f)
        } else {
            canvas.drawRoundRect(coverRect, 14f, 14f, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = withAlpha(CREAM, 40)
            })
            val initial = layoutOf(
                song.title.trim().take(1).ifEmpty { "♪" }, 42f, 700, CREAM,
                coverRect.width().toInt(), maxLines = 1,
                align = android.text.Layout.Alignment.ALIGN_CENTER,
            )
            canvas.save()
            canvas.translate(coverRect.left, coverRect.centerY() - initial.height / 2f)
            initial.draw(canvas)
            canvas.restore()
        }
        val textX = coverRect.right + 30f
        val textWidth = (WIDTH - SIDE_PAD - textX).toInt().coerceAtLeast(1)
        val title = layoutOf(
            song.title, 38.9f, 650, CREAM, textWidth,
            maxLines = 1, lineSpacingMult = 1.25f, letterSpacing = 0.02f,
            ellipsize = TextUtils.TruncateAt.END,
        )
        val artist = layoutOf(
            song.artist, 29.2f, 400, CREAM_DIM, textWidth,
            maxLines = 1, lineSpacingMult = 1.25f, letterSpacing = 0.04f,
            ellipsize = TextUtils.TruncateAt.END,
        )
        val blockHeight = title.height + 5f + artist.height
        val blockTop = coverRect.top + (coverRect.height() - blockHeight) / 2f
        canvas.save()
        canvas.translate(textX, blockTop)
        title.draw(canvas)
        canvas.translate(0f, title.height + 5f)
        artist.draw(canvas)
        canvas.restore()

        // 底部：进度槽 + 居中的「猫爪音乐」字标（不要图标和二维码）
        val brand = layoutOf(
            "猫爪音乐", 44f, 550, withAlpha(CREAM, 158),
            (WIDTH - SIDE_PAD * 2).toInt().coerceAtLeast(1),
            maxLines = 1, letterSpacing = 0.03f,
            align = android.text.Layout.Alignment.ALIGN_CENTER,
            typeface = brandTypeface,
        )
        canvas.save()
        canvas.translate(SIDE_PAD, HEIGHT - 54f - brand.height)
        brand.draw(canvas)
        canvas.restore()

        canvas.drawRoundRect(
            RectF(SIDE_PAD, PROGRESS_Y, WIDTH - SIDE_PAD, PROGRESS_Y + PROGRESS_H),
            PROGRESS_H / 2f,
            PROGRESS_H / 2f,
            Paint(Paint.ANTI_ALIAS_FLAG).apply { color = withAlpha(CREAM, 46) },
        )

        return bitmap
    }

    // ── 视频编码（EGL + Surface） ─────────────────────────────────────────────

    private class VideoEncoder(val codec: MediaCodec, val inputSurface: Surface)

    private class MuxState(val muxer: MediaMuxer) {
        var videoTrack = -1
        var audioTrack = -1
        var started = false
    }

    private class EncodedAudio(val format: MediaFormat, val frames: List<AudioFrame>)

    private class AudioFrame(
        val data: ByteArray,
        val presentationTimeUs: Long,
        val flags: Int,
    )

    /** 把 Bitmap 逐帧画到编码器的输入 Surface 上（Grafika 的 EGL 用法） */
    private class FrameSurfaceRenderer(private val targetSurface: Surface) {

        private var eglDisplay: EGLDisplay = EGL14.EGL_NO_DISPLAY
        private var eglContext: EGLContext = EGL14.EGL_NO_CONTEXT
        private var eglSurface: EGLSurface = EGL14.EGL_NO_SURFACE
        private var program = 0
        private var textureId = 0
        private var textureAllocated = false
        private var aPosition = 0
        private var aTexCoord = 0
        private var uTexture = 0

        private val vertices = floatBuffer(
            floatArrayOf(-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f),
        )
        private val texCoords = floatBuffer(
            // Bitmap 第 0 行在上，GL 的 v=0 在下，这里翻转一次
            floatArrayOf(0f, 1f, 1f, 1f, 0f, 0f, 1f, 0f),
        )

        fun setup() {
            eglDisplay = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY)
            check(eglDisplay != EGL14.EGL_NO_DISPLAY) { "no EGL display" }
            val version = IntArray(2)
            check(EGL14.eglInitialize(eglDisplay, version, 0, version, 1)) { "eglInitialize failed" }

            val configAttribs = intArrayOf(
                EGL14.EGL_RED_SIZE, 8,
                EGL14.EGL_GREEN_SIZE, 8,
                EGL14.EGL_BLUE_SIZE, 8,
                EGL14.EGL_ALPHA_SIZE, 8,
                EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
                EGL14.EGL_NONE,
            )
            val configs = arrayOfNulls<EGLConfig>(1)
            val numConfigs = IntArray(1)
            check(
                EGL14.eglChooseConfig(eglDisplay, configAttribs, 0, configs, 0, 1, numConfigs, 0) &&
                    numConfigs[0] > 0,
            ) { "eglChooseConfig failed" }
            val config = configs[0] ?: error("no EGL config")

            eglContext = EGL14.eglCreateContext(
                eglDisplay, config, EGL14.EGL_NO_CONTEXT,
                intArrayOf(EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE), 0,
            )
            check(eglContext != EGL14.EGL_NO_CONTEXT) { "eglCreateContext failed" }
            eglSurface = EGL14.eglCreateWindowSurface(
                eglDisplay, config, targetSurface, intArrayOf(EGL14.EGL_NONE), 0,
            )
            check(eglSurface != EGL14.EGL_NO_SURFACE) { "eglCreateWindowSurface failed" }
            check(EGL14.eglMakeCurrent(eglDisplay, eglSurface, eglSurface, eglContext)) {
                "eglMakeCurrent failed"
            }

            program = createProgram(VERTEX_SHADER, FRAGMENT_SHADER)
            aPosition = GLES20.glGetAttribLocation(program, "aPosition")
            aTexCoord = GLES20.glGetAttribLocation(program, "aTexCoord")
            uTexture = GLES20.glGetUniformLocation(program, "uTexture")

            val textures = IntArray(1)
            GLES20.glGenTextures(1, textures, 0)
            textureId = textures[0]
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, textureId)
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)

            GLES20.glViewport(0, 0, WIDTH, HEIGHT)
            GLES20.glDisable(GLES20.GL_DEPTH_TEST)
            GLES20.glDisable(GLES20.GL_BLEND)
        }

        fun drawFrame(bitmap: Bitmap, presentationTimeUs: Long) {
            GLES20.glClearColor(0f, 0f, 0f, 1f)
            GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT)

            GLES20.glUseProgram(program)
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, textureId)
            if (!textureAllocated) {
                GLUtils.texImage2D(GLES20.GL_TEXTURE_2D, 0, bitmap, 0)
                textureAllocated = true
            } else {
                GLUtils.texSubImage2D(GLES20.GL_TEXTURE_2D, 0, 0, 0, bitmap)
            }
            GLES20.glUniform1i(uTexture, 0)

            GLES20.glEnableVertexAttribArray(aPosition)
            GLES20.glVertexAttribPointer(aPosition, 2, GLES20.GL_FLOAT, false, 0, vertices)
            GLES20.glEnableVertexAttribArray(aTexCoord)
            GLES20.glVertexAttribPointer(aTexCoord, 2, GLES20.GL_FLOAT, false, 0, texCoords)
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)
            GLES20.glDisableVertexAttribArray(aPosition)
            GLES20.glDisableVertexAttribArray(aTexCoord)

            EGLExt.eglPresentationTimeANDROID(eglDisplay, eglSurface, presentationTimeUs * 1000)
            check(EGL14.eglSwapBuffers(eglDisplay, eglSurface)) { "eglSwapBuffers failed" }
        }

        fun release() {
            // 先删 GL 资源，再解绑 / 销毁 EGL 上下文
            if (program != 0) GLES20.glDeleteProgram(program)
            if (textureId != 0) GLES20.glDeleteTextures(1, intArrayOf(textureId), 0)
            program = 0
            textureId = 0
            if (eglDisplay != EGL14.EGL_NO_DISPLAY) {
                EGL14.eglMakeCurrent(
                    eglDisplay, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT,
                )
                if (eglSurface != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(eglDisplay, eglSurface)
                if (eglContext != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(eglDisplay, eglContext)
                EGL14.eglTerminate(eglDisplay)
            }
            eglDisplay = EGL14.EGL_NO_DISPLAY
            eglContext = EGL14.EGL_NO_CONTEXT
            eglSurface = EGL14.EGL_NO_SURFACE
        }
    }

    private fun createSurfaceEncoder(): VideoEncoder {
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, WIDTH, HEIGHT).apply {
            setInteger(MediaFormat.KEY_BIT_RATE, 12_000_000)
            setInteger(MediaFormat.KEY_FRAME_RATE, FPS)
            setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
        }
        val codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        val surface = codec.createInputSurface()
        codec.start()
        return VideoEncoder(codec, surface)
    }

    private fun drain(codec: MediaCodec, mux: MuxState, info: MediaCodec.BufferInfo, eos: Boolean) {
        var idle = 0
        while (true) {
            val index = codec.dequeueOutputBuffer(info, if (eos) 10_000L else 0L)
            when {
                index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                    if (!eos) return
                    idle++
                    if (idle > 200) return
                }

                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    if (!mux.started) {
                        mux.videoTrack = mux.muxer.addTrack(codec.outputFormat)
                        mux.muxer.start()
                        mux.started = true
                    }
                }

                index >= 0 -> {
                    val buffer = codec.getOutputBuffer(index)
                    if (buffer != null) {
                        if (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) info.size = 0
                        if (info.size > 0 && mux.started) {
                            buffer.position(info.offset)
                            buffer.limit(info.offset + info.size)
                            mux.muxer.writeSampleData(mux.videoTrack, buffer, info)
                        }
                    }
                    codec.releaseOutputBuffer(index, false)
                    if (eos && info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
                }

                else -> if (!eos) return
            }
        }
    }

    private fun writeAudioFrames(
        muxer: MediaMuxer,
        track: Int,
        audio: EncodedAudio,
        onProgress: (Float) -> Unit,
    ) {
        if (audio.frames.isEmpty()) return
        val buffer = ByteBuffer.allocateDirect(audio.frames.maxOf { it.data.size })
        val info = MediaCodec.BufferInfo()
        audio.frames.forEachIndexed { index, frame ->
            buffer.clear()
            buffer.put(frame.data)
            buffer.flip()
            info.set(0, frame.data.size, frame.presentationTimeUs, frame.flags)
            runCatching { muxer.writeSampleData(track, buffer, info) }
            if (index % 100 == 0) onProgress(index.toFloat() / audio.frames.size)
        }
        onProgress(1f)
    }

    // ── 音频：下载 + 解码 / AAC 编码 ──────────────────────────────────────────

    /**
     * 远程 / 在线曲的音频落地。
     *
     * 用 media3 的 DataSource（与播放同一套 OkHttp + 自签证书 + URL 内嵌凭据 + SimpleCache）：
     * 已经播过 / 缓存过的歌几乎是本地读取；没缓存过就整首下载到 `share_audio`。
     * 下载失败返回 null —— 继续导出静音视频，不因为没声音让整个分享失败。
     */
    private suspend fun downloadRemoteAudio(
        context: Context,
        song: Song,
        onProgress: (Float) -> Unit,
    ): File? = withContext(Dispatchers.IO) {
        val dir = File(context.cacheDir, "share_audio").apply { mkdirs() }
        val target = File(dir, "audio-${song.id}.bin")
        if (target.length() > 0) {
            onProgress(1f)
            return@withContext target
        }
        val temp = File(dir, "audio-${song.id}.tmp")
        var source: DataSource? = null
        try {
            // 绕开播放缓存：避免与正在播放的 CacheWriter 抢同一个 key 的锁（分享卡住的主因）
            val ds = RemoteAuthDataSources.upstreamFactory(context).createDataSource().also {
                source = it
            }
            val length = ds.open(DataSpec(Uri.parse(song.filePath)))
            temp.outputStream().use { out ->
                val buffer = ByteArray(256 * 1024)
                var total = 0L
                // 网络卡死时不要让分享永远停在百分比上：超时就回退静音
                val deadline = android.os.SystemClock.elapsedRealtime() + 120_000
                while (true) {
                    ensureActive()
                    if (android.os.SystemClock.elapsedRealtime() > deadline) {
                        throw java.io.IOException("audio download timeout")
                    }
                    val read = ds.read(buffer, 0, buffer.size)
                    if (read < 0) break
                    if (read == 0) continue
                    out.write(buffer, 0, read)
                    total += read
                    if (length > 0) onProgress((total.toFloat() / length).coerceIn(0f, 1f))
                }
            }
            if (temp.length() > 0) {
                temp.renameTo(target)
                trimAudioCache(dir)
                target
            } else {
                temp.delete()
                null
            }
        } catch (t: Throwable) {
            if (t is CancellationException) throw t
            temp.delete()
            null
        } finally {
            runCatching { source?.close() }
        }
    }

    /** 分享音频缓存上限 300MB，超了按最久未用删。 */
    private fun trimAudioCache(dir: File) {
        val limit = 300L * 1024 * 1024
        val files = dir.listFiles()?.filter { it.isFile && it.extension == "bin" } ?: return
        var total = files.sumOf { it.length() }
        if (total <= limit) return
        files.sortedBy { it.lastModified() }.forEach {
            if (total <= limit) return@forEach
            total -= it.length()
            it.delete()
        }
    }

    /** 把解码器输出的 PCM（16 / 32bit、float、8bit）统一成 AAC 编码器要的 16bit 小端。 */
    private fun pcm16Bytes(
        src: ByteBuffer,
        info: MediaCodec.BufferInfo,
        encoding: Int,
    ): ByteArray {
        val dup = src.duplicate()
        dup.position(info.offset)
        dup.limit(info.offset + info.size)
        if (encoding == AudioFormat.ENCODING_PCM_16BIT) {
            val raw = ByteArray(info.size)
            dup.get(raw)
            return raw
        }
        dup.order(ByteOrder.LITTLE_ENDIAN)
        val out = ByteBuffer.allocate(info.size * 2).order(ByteOrder.LITTLE_ENDIAN)
        when (encoding) {
            AudioFormat.ENCODING_PCM_FLOAT -> {
                val floats = dup.asFloatBuffer()
                while (floats.hasRemaining()) {
                    out.putShort((floats.get().coerceIn(-1f, 1f) * 32767f).toInt().toShort())
                }
            }

            AudioFormat.ENCODING_PCM_32BIT -> {
                val ints = dup.asIntBuffer()
                while (ints.hasRemaining()) out.putShort((ints.get() shr 16).toShort())
            }

            AudioFormat.ENCODING_PCM_24BIT_PACKED -> {
                // 3 字节小端一采样：取高 16 位就是 16bit 值
                val sample = ByteArray(3)
                while (dup.remaining() >= 3) {
                    dup.get(sample)
                    val v = (sample[1].toInt() and 0xFF) or ((sample[2].toInt() and 0xFF) shl 8)
                    out.putShort(v.toShort())
                }
            }

            AudioFormat.ENCODING_PCM_8BIT -> {
                while (dup.hasRemaining()) {
                    val v = ((dup.get().toInt() and 0xFF) - 128) shl 8
                    out.putShort(v.toShort())
                }
            }

            else -> {
                dup.position(info.offset)
                val raw = ByteArray(info.size)
                dup.get(raw)
                return raw
            }
        }
        return out.array().copyOf(out.position())
    }

    /**
     * 用 nextlib 打包的 FFmpeg（libavcodec）把 FLAC 解成 16bit PCM，再交给 MediaCodec 编 AAC。
     * 只在 FLAC 且 FFmpeg 可用时走这里；失败返回 null，调用方回退平台解码。
     */
    private fun flacToAac(
        extractor: MediaExtractor,
        srcFormat: MediaFormat,
        startUs: Long,
        endUs: Long,
    ): EncodedAudio? {
        val sampleRate = runCatching { srcFormat.getInteger(MediaFormat.KEY_SAMPLE_RATE) }
            .getOrDefault(44_100)
        val channels = runCatching { srcFormat.getInteger(MediaFormat.KEY_CHANNEL_COUNT) }
            .getOrDefault(2)
        val encRate = sampleRate.coerceIn(8_000, 96_000)
        val encChannels = channels.coerceIn(1, 8)
        val bytesPerSecond = 2L * encChannels * encRate

        var encoder: MediaCodec? = null
        var ffDecoder: FfmpegFlacDecoder? = null
        try {
            val encFormat = MediaFormat.createAudioFormat(
                MediaFormat.MIMETYPE_AUDIO_AAC, encRate, encChannels,
            ).apply {
                setInteger(
                    MediaFormat.KEY_AAC_PROFILE,
                    MediaCodecInfo.CodecProfileLevel.AACObjectLC,
                )
                setInteger(MediaFormat.KEY_BIT_RATE, 192_000)
                setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 64 * 1024)
            }
            val enc = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
            encoder = enc
            enc.configure(encFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            enc.start()

            val frames = ArrayList<AudioFrame>(2048)
            val encInfo = MediaCodec.BufferInfo()
            var aacFormat: MediaFormat? = null
            var pcmPtsUs = 0L

            fun drainEncoder(eos: Boolean) {
                var idle = 0
                while (true) {
                    val index = enc.dequeueOutputBuffer(encInfo, if (eos) 10_000L else 0L)
                    when {
                        index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                            if (!eos) return
                            idle++
                            if (idle > 100) return
                        }

                        index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> aacFormat = enc.outputFormat

                        index >= 0 -> {
                            val isConfig =
                                encInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                            if (encInfo.size > 0 && !isConfig) {
                                val buf = enc.getOutputBuffer(index)
                                if (buf != null) {
                                    val bytes = ByteArray(encInfo.size)
                                    buf.position(encInfo.offset)
                                    buf.get(bytes)
                                    frames.add(
                                        AudioFrame(
                                            bytes,
                                            encInfo.presentationTimeUs,
                                            encInfo.flags,
                                        ),
                                    )
                                }
                            }
                            enc.releaseOutputBuffer(index, false)
                            if (encInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
                        }
                    }
                }
            }

            // PCM 时间自己累加：从 0 开始，天然和视频对齐，不依赖任何解码器 PTS
            fun feedPcm(pcm: ByteBuffer) {
                if (!pcm.hasRemaining()) return
                val pts = pcmPtsUs
                pcmPtsUs += pcm.remaining().toLong() * 1_000_000L / bytesPerSecond
                var offset = pcm.position()
                val end = pcm.limit()
                var waits = 0
                while (offset < end) {
                    val index = enc.dequeueInputBuffer(10_000)
                    if (index < 0) {
                        drainEncoder(eos = false)
                        if (++waits > 50) return
                        continue
                    }
                    val input = enc.getInputBuffer(index) ?: continue
                    input.clear()
                    val chunk = min(end - offset, input.capacity())
                    val slice = pcm.duplicate()
                    slice.position(offset)
                    slice.limit(offset + chunk)
                    input.put(slice)
                    enc.queueInputBuffer(index, 0, chunk, pts, 0)
                    offset += chunk
                }
            }

            fun drainFfmpeg() {
                val dec = ffDecoder ?: return
                while (true) {
                    val out = dec.dequeueOutput() ?: break
                    val data = out.data
                    if (!out.shouldBeSkipped && data != null && data.hasRemaining()) {
                        feedPcm(data)
                    }
                    dec.releaseOutput(out)
                }
            }

            ffDecoder = FfmpegFlacDecoder(
                Format.Builder()
                    .setSampleMimeType(MediaFormat.MIMETYPE_AUDIO_FLAC)
                    .setSampleRate(sampleRate)
                    .setChannelCount(channels)
                    .build(),
            )

            // MediaExtractor 给的是裸 FLAC 帧，FFmpeg 的 flac 解码器可以直接吃
            val readBuffer = ByteBuffer.allocateDirect(1 shl 20)
            var fed = 0
            while (true) {
                val t = extractor.sampleTime
                if (t < 0 || t >= endUs) break
                readBuffer.clear()
                val size = extractor.readSampleData(readBuffer, 0)
                if (size < 0) break

                var input: DecoderInputBuffer? = null
                var waits = 0
                while (input == null) {
                    drainFfmpeg()
                    input = ffDecoder!!.dequeueInput()
                    if (input == null) {
                        if (++waits > 200) break
                        Thread.sleep(2)
                    }
                }
                val target = input ?: break
                target.clear()
                target.timeUs = t
                target.ensureSpaceForWrite(size)
                readBuffer.position(0)
                readBuffer.limit(size)
                target.data!!.put(readBuffer)
                target.flip()
                ffDecoder!!.queueInput(target)
                extractor.advance()
                fed++
                drainFfmpeg()
            }
            drainFfmpeg()

            Log.d(
                TAG,
                "ffmpeg flac: fed=$fed pcmUs=$pcmPtsUs sr=$sampleRate ch=$channels",
            )

            var eosIndex = enc.dequeueInputBuffer(10_000)
            var eosWaits = 0
            while (eosIndex < 0 && eosWaits++ < 20) {
                drainEncoder(eos = false)
                eosIndex = enc.dequeueInputBuffer(10_000)
            }
            if (eosIndex >= 0) {
                enc.queueInputBuffer(
                    eosIndex, 0, 0, pcmPtsUs, MediaCodec.BUFFER_FLAG_END_OF_STREAM,
                )
            }
            drainEncoder(eos = true)

            val format = aacFormat ?: return null
            if (frames.isEmpty()) return null
            return EncodedAudio(format, frames)
        } catch (t: Throwable) {
            Log.w(TAG, "ffmpeg flac transcode failed", t)
            return null
        } finally {
            runCatching { ffDecoder?.release() }
            runCatching { encoder?.stop() }
            runCatching { encoder?.release() }
        }
    }

    /** 读 WAV 头拿真实采样率 / 声道 / PCM 编码；不是 WAV 返回 null。 */
    private fun probeWav(path: String): Triple<Int, Int, Int>? = runCatching {
        val file = File(path)
        if (!file.isFile) return@runCatching null
        file.inputStream().use { ins ->
            val header = ByteArray(12)
            var read = 0
            while (read < 12) {
                val n = ins.read(header, read, 12 - read)
                if (n <= 0) return@runCatching null
                read += n
            }
            if (String(header, 0, 4, Charsets.US_ASCII) != "RIFF") return@runCatching null
            if (String(header, 8, 4, Charsets.US_ASCII) != "WAVE") return@runCatching null

            val chunk = ByteArray(8)
            var result: Triple<Int, Int, Int>? = null
            loop@ while (true) {
                var got = 0
                while (got < 8) {
                    val n = ins.read(chunk, got, 8 - got)
                    if (n <= 0) break@loop
                    got += n
                }
                val id = String(chunk, 0, 4, Charsets.US_ASCII)
                val size = ByteBuffer.wrap(chunk).order(ByteOrder.LITTLE_ENDIAN).getInt(4)
                if (size < 0) break@loop
                if (id == "fmt ") {
                    val fmtSize = size.coerceIn(16, 64)
                    val fmt = ByteArray(fmtSize)
                    var f = 0
                    while (f < fmtSize) {
                        val n = ins.read(fmt, f, fmtSize - f)
                        if (n <= 0) break@loop
                        f += n
                    }
                    val bb = ByteBuffer.wrap(fmt).order(ByteOrder.LITTLE_ENDIAN)
                    var tag = bb.getShort(0).toInt() and 0xFFFF
                    val ch = bb.getShort(2).toInt() and 0xFFFF
                    val rate = bb.getInt(4)
                    val bits = bb.getShort(14).toInt() and 0xFFFF
                    // WAVE_FORMAT_EXTENSIBLE：真实格式在 subformat GUID 的前 2 字节
                    if (tag == 0xFFFE && fmtSize >= 26) {
                        tag = bb.getShort(24).toInt() and 0xFFFF
                    }
                    val encoding = when {
                        tag == 3 -> AudioFormat.ENCODING_PCM_FLOAT
                        bits <= 8 -> AudioFormat.ENCODING_PCM_8BIT
                        bits <= 16 -> AudioFormat.ENCODING_PCM_16BIT
                        bits <= 24 -> AudioFormat.ENCODING_PCM_24BIT_PACKED
                        else -> AudioFormat.ENCODING_PCM_32BIT
                    }
                    result = Triple(rate, ch, encoding)
                    break@loop
                }
                // 跳过非 fmt 的 chunk（块大小按偶数对齐）
                var toSkip = size.toLong() + (size and 1)
                while (toSkip > 0) {
                    val n = ins.skip(toSkip)
                    if (n <= 0) break@loop
                    toSkip -= n
                }
            }
            result
        }
    }.getOrNull()

    /** 16bit PCM 重采样（只用于降采样）。整数倍用盒式平均，其余线性插值。 */
    private fun resample16(src: ByteArray, channels: Int, from: Int, to: Int): ByteArray {
        val frameBytes = 2 * channels
        val frames = src.size / frameBytes
        if (frames <= 0 || from == to) return src
        val step = from.toDouble() / to
        val outFrames = (frames / step).toInt().coerceAtLeast(1)
        val out = ByteArray(outFrames * frameBytes)
        val inBuf = ByteBuffer.wrap(src).order(ByteOrder.LITTLE_ENDIAN)
        val outBuf = ByteBuffer.wrap(out).order(ByteOrder.LITTLE_ENDIAN)
        val box = if (step >= 2.0) step.toInt().coerceAtLeast(1) else 0
        var pos = 0.0
        for (i in 0 until outFrames) {
            val i0 = pos.toInt().coerceAtMost(frames - 1)
            val i1 = (i0 + 1).coerceAtMost(frames - 1)
            val frac = pos - i0
            for (ch in 0 until channels) {
                val v = if (box > 0) {
                    var sum = 0
                    for (k in 0 until box) {
                        val idx = (i0 + k).coerceAtMost(frames - 1)
                        sum += inBuf.getShort(idx * frameBytes + ch * 2).toInt()
                    }
                    sum / box
                } else {
                    val s0 = inBuf.getShort(i0 * frameBytes + ch * 2).toInt()
                    val s1 = inBuf.getShort(i1 * frameBytes + ch * 2).toInt()
                    (s0 + (s1 - s0) * frac).toInt()
                }
                outBuf.putShort(
                    i * frameBytes + ch * 2,
                    v.coerceIn(-32768, 32767).toShort(),
                )
            }
            pos += step
        }
        return out
    }

    /** WAV / 裸 PCM（audio/raw 或 mime 为空）：直接读 PCM，转 16bit 后编 AAC。 */
    private fun pcmToAac(
        extractor: MediaExtractor,
        srcFormat: MediaFormat,
        startUs: Long,
        endUs: Long,
        failLog: MutableList<String>,
        path: String,
        debug: MutableList<String>,
    ): EncodedAudio? {
        // 容器报的采样率不一定对（有的 raw 轨干脆没有），WAV 头才是准的。
        // 之前按错误的低采样率编码，就会「女声变男低音 + 音画不同步」。
        val probed = probeWav(path)
        val formatRate = runCatching { srcFormat.getInteger(MediaFormat.KEY_SAMPLE_RATE) }
            .getOrDefault(-1)
        val formatCh = runCatching { srcFormat.getInteger(MediaFormat.KEY_CHANNEL_COUNT) }
            .getOrDefault(-1)
        val formatEncoding = runCatching { srcFormat.getInteger(MediaFormat.KEY_PCM_ENCODING) }
            .getOrDefault(AudioFormat.ENCODING_PCM_16BIT)
        val sampleRate = probed?.first ?: formatRate.takeIf { it > 0 } ?: 44_100
        val channels = probed?.second ?: formatCh.takeIf { it > 0 } ?: 2
        val pcmEncoding = probed?.third ?: formatEncoding
        if (sampleRate !in 8_000..96_000 || channels !in 1..8) {
            failLog.add("采样率/声道不支持($sampleRate/$channels)")
            return null
        }
        // 部分机型的 AAC 编码器对 96k 支持不好（会按 48k 处理，导致慢放 + 低八度），
        // raw 这里统一重采样到 48k 再编码，规避差异。
        val targetRate = if (sampleRate > 48_000) 48_000 else sampleRate
        val encRate = targetRate
        val encChannels = channels
        val bytesPerSecond = 2L * encChannels * encRate
        debug.add(
            "raw fmt=$formatRate/$formatCh enc=$formatEncoding probe=$probed " +
                "src=$sampleRate used=$encRate/$encChannels",
        )

        var encoder: MediaCodec? = null
        try {
            val encFormat = MediaFormat.createAudioFormat(
                MediaFormat.MIMETYPE_AUDIO_AAC, encRate, encChannels,
            ).apply {
                setInteger(
                    MediaFormat.KEY_AAC_PROFILE,
                    MediaCodecInfo.CodecProfileLevel.AACObjectLC,
                )
                setInteger(MediaFormat.KEY_BIT_RATE, 192_000)
                setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 64 * 1024)
            }
            val enc = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
            encoder = enc
            enc.configure(encFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            enc.start()

            val frames = ArrayList<AudioFrame>(2048)
            val encInfo = MediaCodec.BufferInfo()
            var aacFormat: MediaFormat? = null
            var pcmPtsUs = 0L

            fun drain(eos: Boolean) {
                var idle = 0
                while (true) {
                    val index = enc.dequeueOutputBuffer(encInfo, if (eos) 10_000L else 0L)
                    when {
                        index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                            if (!eos) return
                            if (++idle > 100) return
                        }

                        index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> aacFormat = enc.outputFormat

                        index >= 0 -> {
                            val cfg = encInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                            if (encInfo.size > 0 && !cfg) {
                                val buf = enc.getOutputBuffer(index)
                                if (buf != null) {
                                    val bytes = ByteArray(encInfo.size)
                                    buf.position(encInfo.offset)
                                    buf.get(bytes)
                                    frames.add(
                                        AudioFrame(
                                            bytes,
                                            encInfo.presentationTimeUs,
                                            encInfo.flags,
                                        ),
                                    )
                                }
                            }
                            enc.releaseOutputBuffer(index, false)
                            if (encInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
                        }
                    }
                }
            }

            fun feed(pcm: ByteArray) {
                if (pcm.isEmpty()) return
                val pts = pcmPtsUs
                pcmPtsUs += pcm.size.toLong() * 1_000_000L / bytesPerSecond
                var offset = 0
                var waits = 0
                while (offset < pcm.size) {
                    val index = enc.dequeueInputBuffer(10_000)
                    if (index < 0) {
                        drain(eos = false)
                        if (++waits > 50) return
                        continue
                    }
                    val input = enc.getInputBuffer(index) ?: continue
                    input.clear()
                    val chunk = min(pcm.size - offset, input.capacity())
                    input.put(pcm, offset, chunk)
                    enc.queueInputBuffer(index, 0, chunk, pts, 0)
                    offset += chunk
                }
            }

            extractor.seekTo(startUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC)
            val read = ByteBuffer.allocateDirect(1 shl 20)
            val info = MediaCodec.BufferInfo()
            var fed = 0
            var fedBytes = 0L
            val pcmAll = java.io.ByteArrayOutputStream()
            while (true) {
                val t = extractor.sampleTime
                if (t < 0 || t >= endUs) break
                read.clear()
                val size = extractor.readSampleData(read, 0)
                if (size < 0) break
                info.set(0, size, 0, 0)
                val pcm = pcm16Bytes(read, info, pcmEncoding)
                pcmAll.write(pcm)
                fedBytes += pcm.size
                extractor.advance()
                fed++
            }
            val rawPcm = pcmAll.toByteArray()
            if (sampleRate != encRate) {
                feed(resample16(rawPcm, channels, sampleRate, encRate))
            } else {
                feed(rawPcm)
            }
            val durSec = ((endUs - startUs).coerceAtLeast(1)) / 1_000_000.0
            val estRate = (fedBytes / (2.0 * channels) / durSec).toInt()
            debug.add("bytes=$fedBytes estRate=$estRate")

            var eosIndex = enc.dequeueInputBuffer(10_000)
            var eosWaits = 0
            while (eosIndex < 0 && eosWaits++ < 20) {
                drain(eos = false)
                eosIndex = enc.dequeueInputBuffer(10_000)
            }
            if (eosIndex >= 0) {
                enc.queueInputBuffer(
                    eosIndex, 0, 0, pcmPtsUs, MediaCodec.BUFFER_FLAG_END_OF_STREAM,
                )
            }
            drain(eos = true)
            debug.add(
                "aacSr=" + runCatching { aacFormat?.getInteger(MediaFormat.KEY_SAMPLE_RATE) }
                    .getOrNull(),
            )

            Log.d(TAG, "pcm: fed=$fed pcmUs=$pcmPtsUs sr=$sampleRate ch=$channels enc=$pcmEncoding")
            val format = aacFormat
            if (format == null) {
                failLog.add("AAC 编码器无输出")
                return null
            }
            if (frames.isEmpty()) {
                failLog.add("没有解码出 PCM")
                return null
            }
            return EncodedAudio(format, frames)
        } catch (t: Throwable) {
            Log.w(TAG, "pcm transcode failed", t)
            failLog.add("PCM 转码异常: " + (t.message ?: t.javaClass.simpleName))
            return null
        } finally {
            runCatching { encoder?.stop() }
            runCatching { encoder?.release() }
        }
    }

    /**
     * 源音频 → PCM → AAC。缓冲所有编码后的帧，交给调用方在 muxer start 后统一写入。
     * 任何一步不支持（浮点 PCM、未知编码）都返回 null，由调用方回退静音。
     */
    private fun transcodeAudio(
        context: Context,
        song: Song,
        localAudio: File?,
        startUs: Long,
        endUs: Long,
        failLog: MutableList<String>,
        debug: MutableList<String>,
    ): EncodedAudio? {
        val path = localAudio?.absolutePath ?: song.filePath
        if (path.isBlank()) {
            failLog.add("音频路径为空")
            return null
        }
        if (localAudio == null && path.startsWith("http", ignoreCase = true)) {
            failLog.add("在线音频未下载")
            return null
        }

        var extractor: MediaExtractor? = null
        var decoder: MediaCodec? = null
        var encoder: MediaCodec? = null
        try {
            val ex = MediaExtractor()
            extractor = ex
            if (path.startsWith("content://")) {
                ex.setDataSource(context, Uri.parse(path), null)
            } else {
                ex.setDataSource(path)
            }

            var trackIndex = -1
            for (i in 0 until ex.trackCount) {
                val mime = ex.getTrackFormat(i).getString(MediaFormat.KEY_MIME).orEmpty()
                if (mime.startsWith("audio/")) {
                    trackIndex = i
                    break
                }
            }
            if (trackIndex < 0) {
                failLog.add("找不到音轨")
                return null
            }
            val srcFormat = ex.getTrackFormat(trackIndex)
            val rawMime = srcFormat.getString(MediaFormat.KEY_MIME).orEmpty()
            // 有些 FLAC 报 audio/x-flac，MediaCodec 只认 audio/flac
            val mime = if (rawMime.equals("audio/x-flac", true)) "audio/flac" else rawMime
            val isFlac = mime == "audio/flac"
            ex.selectTrack(trackIndex)

            // WAV / 裸 PCM（MediaExtractor 报 audio/raw 或 mime 为空）：没有解码器可建，
            // 直接按 PCM 读出来转 AAC。在线曲偶尔会拿到这种「无损」直链。
            if (rawMime.isBlank() || rawMime.startsWith("audio/raw")) {
                return pcmToAac(ex, srcFormat, startUs, endUs, failLog, path, debug)
            }
            if (isFlac) {
                // 平台 MediaExtractor 对 FLAC 的 seekTo 不可靠：有的直接返回 -1 并把游标留在 EOS，
                // 结果一帧 PCM 都拿不到（视频就没声音）。这里不 seek，从 0 顺序 advance 到起始点，
                // FLAC 帧自带头、可独立解码，跳过前面的样本是安全的。
                while (true) {
                    val t = ex.sampleTime
                    if (t < 0 || t >= startUs) break
                    ex.advance()
                }
            } else {
                ex.seekTo(startUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC)
            }

            // FLAC 优先用项目里已经打包的 FFmpeg（nextlib 的 libavcodec）解码，
            // 绕开各机型的平台 FLAC 解码器差异。
            if (isFlac && FfmpegLibrary.supportsFormat(MediaFormat.MIMETYPE_AUDIO_FLAC)) {
                val viaFfmpeg = flacToAac(ex, srcFormat, startUs, endUs)
                if (viaFfmpeg != null) return viaFfmpeg
                failLog.add("FFmpeg 解码失败")
                // FFmpeg 失败才回退平台解码：重置提取器并重新跳过到起始点
                runCatching { ex.seekTo(0, MediaExtractor.SEEK_TO_CLOSEST_SYNC) }
                while (true) {
                    val t = ex.sampleTime
                    if (t < 0 || t >= startUs) break
                    ex.advance()
                }
            } else if (isFlac) {
                failLog.add("FFmpeg 不可用")
            }

            val dec = MediaCodec.createDecoderByType(mime)
            decoder = dec
            // 不向解码器指定 PCM 编码：各家 FLAC 解码器支持度不一，指定反而可能 configure 失败。
            // 解码出来的 16/24/32bit、float 统一在 pcm16Bytes 里转成 16bit。
            dec.configure(srcFormat, null, null, 0)
            dec.start()

            var enc: MediaCodec? = null
            var encFormat: MediaFormat? = null
            var encStarted = false
            var encEosQueued = false
            var decEos = false
            var encEos = false
            var lastPts = 0L
            var pcmPtsUs = 0L
            var audioSampleRate = 0
            var audioChannels = 0
            var pcmEncoding = AudioFormat.ENCODING_PCM_16BIT
            val frames = ArrayList<AudioFrame>(2048)
            val decInfo = MediaCodec.BufferInfo()
            val encInfo = MediaCodec.BufferInfo()

            fun ensureEncoder(sampleRate: Int, channels: Int) {
                if (encStarted) return
                audioSampleRate = sampleRate.coerceIn(8_000, 96_000)
                audioChannels = channels.coerceIn(1, 8)
                val format = MediaFormat.createAudioFormat(
                    MediaFormat.MIMETYPE_AUDIO_AAC,
                    sampleRate.coerceIn(8_000, 96_000),
                    channels.coerceIn(1, 8),
                ).apply {
                    setInteger(
                        MediaFormat.KEY_AAC_PROFILE,
                        MediaCodecInfo.CodecProfileLevel.AACObjectLC,
                    )
                    setInteger(MediaFormat.KEY_BIT_RATE, 192_000)
                    setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 64 * 1024)
                }
                val codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
                codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
                codec.start()
                enc = codec
                encoder = codec
                encStarted = true
            }

            fun drainEncoder(eos: Boolean) {
                val codec = enc ?: return
                var idle = 0
                while (true) {
                    val index = codec.dequeueOutputBuffer(encInfo, if (eos) 10_000L else 0L)
                    when {
                        index == MediaCodec.INFO_TRY_AGAIN_LATER -> {
                            if (!eos) return
                            idle++
                            if (idle > 100) return
                        }

                        index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                            encFormat = codec.outputFormat
                        }

                        index >= 0 -> {
                            val isConfig = encInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                            if (encInfo.size > 0 && !isConfig) {
                                val buf = codec.getOutputBuffer(index)
                                if (buf != null) {
                                    val bytes = ByteArray(encInfo.size)
                                    buf.position(encInfo.offset)
                                    buf.get(bytes)
                                    frames.add(
                                        AudioFrame(bytes, encInfo.presentationTimeUs, encInfo.flags),
                                    )
                                }
                            }
                            codec.releaseOutputBuffer(index, false)
                            if (encInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) {
                                encEos = true
                                return
                            }
                        }
                    }
                }
            }

            fun feedPcm(pcm: ByteBuffer, info: MediaCodec.BufferInfo) {
                val codec = enc ?: return
                val data = pcm16Bytes(pcm, info, pcmEncoding)
                // 不用解码器给的 PTS：部分解码器（尤其 FLAC）时间戳不可靠。
                // 我们是从起始点开始连续喂的，直接按 PCM 采样数累加，天然从 0 对齐视频。
                val pts = pcmPtsUs
                if (audioSampleRate > 0 && audioChannels > 0) {
                    pcmPtsUs += data.size.toLong() * 1_000_000L /
                        (2L * audioChannels * audioSampleRate)
                }
                lastPts = pts
                var offset = 0
                var waits = 0
                while (offset < data.size) {
                    val index = codec.dequeueInputBuffer(10_000)
                    if (index < 0) {
                        drainEncoder(eos = false)
                        if (++waits > 50) return
                        continue
                    }
                    val input = codec.getInputBuffer(index) ?: continue
                    input.clear()
                    val chunk = min(data.size - offset, input.capacity())
                    input.put(data, offset, chunk)
                    codec.queueInputBuffer(index, 0, chunk, pts, 0)
                    offset += chunk
                }
            }

            var inputDone = false
            var guard = 0
            val deadline = android.os.SystemClock.elapsedRealtime() + 60_000
            while (!encEos && guard < 200_000) {
                guard++
                if (android.os.SystemClock.elapsedRealtime() > deadline) break
                if (!inputDone) {
                    val index = dec.dequeueInputBuffer(10_000)
                    if (index >= 0) {
                        val input = dec.getInputBuffer(index)
                        val sampleTime = ex.sampleTime
                        if (input == null) {
                            dec.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                            inputDone = true
                        } else if (sampleTime < 0 || sampleTime >= endUs) {
                            dec.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                            inputDone = true
                        } else {
                            val size = ex.readSampleData(input, 0)
                            if (size < 0) {
                                dec.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                                inputDone = true
                            } else {
                                dec.queueInputBuffer(index, 0, size, sampleTime, 0)
                                ex.advance()
                            }
                        }
                    }
                }

                // 解码器 → PCM → AAC 编码器
                while (true) {
                    val index = dec.dequeueOutputBuffer(decInfo, 0)
                    if (index == MediaCodec.INFO_TRY_AGAIN_LATER) break
                    if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                        val out = dec.outputFormat
                        pcmEncoding = if (out.containsKey(MediaFormat.KEY_PCM_ENCODING)) {
                            out.getInteger(MediaFormat.KEY_PCM_ENCODING)
                        } else {
                            AudioFormat.ENCODING_PCM_16BIT
                        }
                        ensureEncoder(
                            out.getInteger(MediaFormat.KEY_SAMPLE_RATE),
                            out.getInteger(MediaFormat.KEY_CHANNEL_COUNT),
                        )
                        continue
                    }
                    if (index < 0) continue
                    val pts = decInfo.presentationTimeUs
                    // FLAC 已经顺序跳过起始点前的样本，PTS 不可信就不过滤；其他格式仍按 PTS 对齐
                    if (decInfo.size > 0 && (isFlac || (pts >= startUs && pts < endUs))) {
                        val pcm = dec.getOutputBuffer(index)
                        if (pcm != null) feedPcm(pcm, decInfo)
                    }
                    dec.releaseOutputBuffer(index, false)
                    if (decInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) {
                        decEos = true
                        break
                    }
                }

                drainEncoder(eos = false)

                if (decEos && !encEosQueued) {
                    if (!encStarted) break
                    val codec = enc
                    if (codec != null) {
                        val index = codec.dequeueInputBuffer(10_000)
                        if (index >= 0) {
                            codec.queueInputBuffer(
                                index, 0, 0, lastPts, MediaCodec.BUFFER_FLAG_END_OF_STREAM,
                            )
                            encEosQueued = true
                        }
                    }
                }
            }

            if (!encStarted) {
                failLog.add("平台解码无输出")
                return null
            }
            drainEncoder(eos = true)
            val format = encFormat
            if (format == null) {
                failLog.add("AAC 编码器无输出")
                return null
            }
            if (frames.isEmpty()) {
                failLog.add("没有解码出 PCM")
                return null
            }
            Log.d(TAG, "audio ok: mime=$mime frames=${frames.size} sr=$audioSampleRate ch=$audioChannels")
            return EncodedAudio(format, frames)
        } catch (t: Throwable) {
            Log.w(TAG, "audio transcode failed", t)
            failLog.add("解码异常: " + (t.message ?: t.javaClass.simpleName))
            return null
        } finally {
            runCatching { decoder?.stop() }
            runCatching { decoder?.release() }
            runCatching { encoder?.stop() }
            runCatching { encoder?.release() }
            runCatching { extractor?.release() }
        }
    }

    private const val VERTEX_SHADER = """
        attribute vec4 aPosition;
        attribute vec2 aTexCoord;
        varying vec2 vTexCoord;
        void main() {
            gl_Position = aPosition;
            vTexCoord = aTexCoord;
        }
    """

    private const val FRAGMENT_SHADER = """
        precision mediump float;
        uniform sampler2D uTexture;
        varying vec2 vTexCoord;
        void main() {
            gl_FragColor = texture2D(uTexture, vTexCoord);
        }
    """

    private fun createProgram(vertexSource: String, fragmentSource: String): Int {
        val vertex = compileShader(GLES20.GL_VERTEX_SHADER, vertexSource)
        val fragment = compileShader(GLES20.GL_FRAGMENT_SHADER, fragmentSource)
        val program = GLES20.glCreateProgram()
        check(program != 0) { "glCreateProgram failed" }
        GLES20.glAttachShader(program, vertex)
        GLES20.glAttachShader(program, fragment)
        GLES20.glLinkProgram(program)
        val status = IntArray(1)
        GLES20.glGetProgramiv(program, GLES20.GL_LINK_STATUS, status, 0)
        check(status[0] == GLES20.GL_TRUE) { "program link failed: " + GLES20.glGetProgramInfoLog(program) }
        GLES20.glDeleteShader(vertex)
        GLES20.glDeleteShader(fragment)
        return program
    }

    private fun compileShader(type: Int, source: String): Int {
        val shader = GLES20.glCreateShader(type)
        check(shader != 0) { "glCreateShader failed" }
        GLES20.glShaderSource(shader, source)
        GLES20.glCompileShader(shader)
        val status = IntArray(1)
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, status, 0)
        check(status[0] == GLES20.GL_TRUE) { "shader compile failed: " + GLES20.glGetShaderInfoLog(shader) }
        return shader
    }

    private fun floatBuffer(values: FloatArray): FloatBuffer =
        ByteBuffer.allocateDirect(values.size * 4)
            .order(ByteOrder.nativeOrder())
            .asFloatBuffer()
            .apply {
                put(values)
                position(0)
            }
}
