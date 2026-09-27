package com.catclaw.music.kt.data.lyrics

import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.Rect
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.provider.Settings
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.net.toUri
import com.catclaw.music.kt.core.lyrics.LyricPresentation
import com.catclaw.music.kt.core.lyrics.Lyrics
import com.catclaw.music.kt.core.lyrics.LyricsState
import com.catclaw.music.kt.core.playback.PlayerController
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.roundToInt
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/** 是否授予「显示在其他应用上层」 */
object OverlayPermission {
    fun granted(context: Context): Boolean = Settings.canDrawOverlays(context)

    fun requestIntent(context: Context): Intent =
        Intent(
            Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            ("package:" + context.packageName).toUri(),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
}

/**
 * 桌面歌词悬浮窗。窗口参数与交互对齐 C# `DesktopLyricService.cs`：
 * `TYPE_APPLICATION_OVERLAY` + `NOT_FOCUSABLE | NOT_TOUCH_MODAL | LAYOUT_NO_LIMITS`，
 * 宽 MATCH_PARENT / 高 WRAP_CONTENT，gravity = TOP|CENTER_HORIZONTAL，Y = 屏高 × 比例；
 * 正文 + 彩字两层 TextView 做逐字填充；纵向拖动改位置（存比例不存像素）；
 * 锁定后加 `NOT_TOUCHABLE` 让触摸穿透到下层。
 */
@Singleton
class DesktopLyricController @Inject constructor(
    @ApplicationContext private val context: Context,
    private val settings: DesktopLyricSettings,
    private val status: DesktopLyricStatus,
) {

    private val windowManager = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
    private val density = context.resources.displayMetrics.density

    private val container = LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
        gravity = Gravity.CENTER_HORIZONTAL
        setPadding(dp(14), dp(6), dp(14), dp(6))
    }
    private val textBase = TextView(context).apply {
        maxLines = 2
        ellipsize = TextUtils.TruncateAt.END
        gravity = Gravity.CENTER
        includeFontPadding = false
    }
    private val textFill = TextView(context).apply {
        maxLines = 2
        ellipsize = TextUtils.TruncateAt.END
        gravity = Gravity.CENTER
        includeFontPadding = false
    }
    private val textFrame = FrameLayout(context).apply {
        addView(textBase, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT))
        addView(textFill, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT))
    }

    private var params: WindowManager.LayoutParams? = null
    private var attached = false

    /** 窗口被系统静默拒绝时的回调（宿主用它把持久化的"开"回滚掉） */
    var onAttachRejected: (() -> Unit)? = null

    private val _visible = MutableStateFlow(false)
    val visible: StateFlow<Boolean> = _visible.asStateFlow()

    private var dragStartY = 0f
    private var dragStartParamsY = 0

    fun show(): Boolean {
        if (attached) return true
        if (!OverlayPermission.granted(context)) return false

        val screenHeight = context.resources.displayMetrics.heightPixels
        params = WindowManager.LayoutParams(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.RGBA_8888,
        ).apply {
            gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            y = (screenHeight * settings.posYRatio.value).roundToInt()
        }

        container.removeAllViews()
        container.addView(textFrame)
        applyTextStyle()
        applyBackground()
        container.setOnTouchListener(dragListener)
        applyLock(settings.locked.value)

        // 锁定状态下视图不接收事件，锁定按钮也就点不动——与 C# 一致：先解锁再点
        runCatching { windowManager.addView(container, params) }.onFailure { return false }
        attached = true
        _visible.value = true
        status.setAttached(true)
        // addView 不抛 **不等于** 窗口真挂上了：Android 14+/MIUI 的悬浮窗授权是限时的，
        // `cmd appops get` 的 mode 仍然是 allow、`Settings.canDrawOverlays()` 也返回 true，
        // 系统却只在展示阶段静默拒绝 —— 真机上量到的就是"标签显示开、屏幕上什么都没有"。
        // 所以挂上之后回到 UI 线程验一次 window token，没挂上就通知宿主回滚。
        container.post {
            if (!attached) return@post
            if (!container.isAttachedToWindow || container.windowToken == null) {
                status.setAttached(false)
                runCatching { onAttachRejected?.invoke() }
            }
        }
        settings.onPosYChanged = { y -> moveVertical(y) }
        settings.onLockChanged = { locked -> applyLock(locked) }
        settings.onStyleChanged = { refreshStyle() }
        return true
    }

    fun hide() {
        if (!attached) return
        runCatching { windowManager.removeViewImmediate(container) }
        attached = false
        _visible.value = false
        status.setAttached(false)
        settings.onPosYChanged = null
        settings.onLockChanged = null
        settings.onStyleChanged = null
    }

    /** 设置页改动字号/配色后立刻重绘，不必重建窗口 */
    fun refreshStyle() {
        if (!attached) return
        applyTextStyle()
        applyBackground()
    }

    /** 用当前行 + 填充进度刷新；空行时保留上一行不闪空 */
    fun update(presentation: LyricPresentation?) {
        val text = presentation?.line?.text?.takeIf { it.isNotBlank() } ?: return
        if (textBase.text != text) {
            textBase.text = text
            textFill.text = text
        }
        val progress = presentation.fillProgress.toFloat().coerceIn(0f, 1f)
        textFill.post {
            if (!attached) return@post
            textFill.clipBounds = Rect(0, 0, (textFill.width * progress).toInt(), textFill.height)
            textFill.invalidate()
        }
    }

    fun updateFor(lyrics: Lyrics?, positionMs: Long) {
        update(LyricsState.present(lyrics, positionMs))
    }

    private fun moveVertical(y: Int) {
        val p = params ?: return
        p.y = y
        if (attached) runCatching { windowManager.updateViewLayout(container, p) }
    }

    private val dragListener = View.OnTouchListener { _, event ->
        val p = params ?: return@OnTouchListener false
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                dragStartY = event.rawY
                dragStartParamsY = p.y
                true
            }
            MotionEvent.ACTION_MOVE -> {
                val next = (dragStartParamsY + (event.rawY - dragStartY)).roundToInt()
                val max = context.resources.displayMetrics.heightPixels
                moveVertical(next.coerceIn(0, max))
                true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                settings.savePosYRatio(
                    ratio = p.y.toFloat() / context.resources.displayMetrics.heightPixels,
                    screenHeightPx = context.resources.displayMetrics.heightPixels,
                )
                true
            }
            else -> false
        }
    }

    private fun applyLock(locked: Boolean) {
        val p = params ?: return
        p.flags = if (locked) {
            p.flags or WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
        } else {
            p.flags and WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE.inv()
        }
        if (attached) runCatching { windowManager.updateViewLayout(container, p) }
    }

    private fun applyTextStyle() {
        listOf(textBase, textFill).forEach {
            it.setTextSize(TypedValue.COMPLEX_UNIT_SP, settings.fontSizeSp.value)
        }
        textBase.setTextColor(settings.textColor.value)
        textFill.setTextColor(settings.highlightColor.value)
    }

    private fun applyBackground() {
        val opacity = settings.bgOpacity.value
        if (opacity <= 0.01f) {
            container.setBackgroundColor(Color.TRANSPARENT)
            return
        }
        container.background = GradientDrawable().apply {
            setColor(Color.argb((opacity * 255).roundToInt(), 0, 0, 0))
            cornerRadius = dp(16).toFloat()
        }
    }

    private fun dp(value: Int): Int = (value * density).roundToInt()
}

/**
 * 把播放状态推给悬浮窗：切歌时读歌词，播放中按进度刷填充。
 * 由前台服务启动，因此 App 退到后台 / 锁屏继续播放时桌面歌词仍然更新。
 */
@Singleton
class DesktopLyricHost @Inject constructor(
    private val controller: DesktopLyricController,
    private val settings: DesktopLyricSettings,
    private val player: PlayerController,
    private val reader: LyricsReader,
) {
    private var job: Job? = null
    private var loadedSongId: Int? = null
    private var loadedLyrics: Lyrics? = null

    fun start(scope: CoroutineScope) {
        if (job?.isActive == true) return
        job = scope.launch(Dispatchers.Main.immediate) {
            // 窗口被系统静默拒绝时，把持久化的"开"回滚掉 —— 否则标签会一直撒谎
            controller.onAttachRejected = { settings.setEnabled(false) }
            launch {
                settings.enabled.collect { enabled ->
                    if (enabled) {
                        // show() 会因悬浮窗权限失效而返回 false（Android 14+ 与 MIUI 的授权是限时的，
                        // 过期后 canDrawOverlays 变 false）。不接住这个 false 的话，
                        // 持久化的"开"会一直挂在设置上，界面显示「桌面歌词：开」而屏幕上什么都没有。
                        if (!controller.show()) settings.setEnabled(false)
                    } else {
                        controller.hide()
                    }
                }
            }
            launch {
                player.state.collect { state ->
                    val song = state.currentSong
                    if (song != null && song.id != loadedSongId) {
                        loadedSongId = song.id
                        loadedLyrics = runCatching { reader.loadFor(song) }.getOrNull()
                    }
                    if (song == null) {
                        loadedSongId = null
                        loadedLyrics = null
                    }
                }
            }
            launch {
                player.positionMsFlow(200).collect { position ->
                    if (controller.visible.value) {
                        controller.updateFor(loadedLyrics, position)
                    }
                }
            }
        }
    }

    fun stop() {
        job?.cancel()
        job = null
        controller.hide()
    }
}
