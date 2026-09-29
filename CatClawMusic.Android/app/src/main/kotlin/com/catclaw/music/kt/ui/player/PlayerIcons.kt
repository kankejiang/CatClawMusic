package com.catclaw.music.kt.ui.player

import androidx.annotation.DrawableRes
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import com.catclaw.music.kt.R

/**
 * 播放页图标：**直接用 MAUI 那批 `ic_*.svg`**，不自己画。
 *
 * 转换方式：`CatClawMusic.Maui/Resources/Images/ic_*.svg` → `app/src/main/res/drawable/ic_*.xml`
 * （VectorDrawable，viewBox 当 viewportWidth/Height，`fill` 当 `android:fillColor`）。
 * 颜色由 `Icon(tint=…)` 覆盖，所以 fillColor 只是兜底。
 *
 * 播放模式与图标的对应照 MAUI `NowPlayingViewModel.Playback.cs:392-397`：
 * ListRepeat→ic_repeat_all、SingleRepeat→ic_repeat_one、Shuffle→ic_shuffle、Sequential→ic_repeat_all。
 */
class PlayerIcon(@DrawableRes val resId: Int)

/** 与 material3 的 `Icon` 同名不同参，调用点写法不变 */
@Composable
fun Icon(imageVector: PlayerIcon, contentDescription: String?, tint: Color, modifier: Modifier = Modifier) {
    Icon(
        painter = painterResource(imageVector.resId),
        contentDescription = contentDescription,
        tint = tint,
        modifier = modifier,
    )
}

object PlayerIcons {
    val Play = PlayerIcon(R.drawable.ic_play)
    val Pause = PlayerIcon(R.drawable.ic_pause)
    val Previous = PlayerIcon(R.drawable.ic_skip_previous)
    val Next = PlayerIcon(R.drawable.ic_skip_next)
    val Sequential = PlayerIcon(R.drawable.ic_repeat_all)
    val Shuffle = PlayerIcon(R.drawable.ic_shuffle)
    val Repeat = PlayerIcon(R.drawable.ic_repeat_all)
    val RepeatOne = PlayerIcon(R.drawable.ic_repeat_one)
    val QueueMusic = PlayerIcon(R.drawable.ic_playlist)
    val Lyrics = PlayerIcon(R.drawable.ic_lyrics)
    val DesktopLyrics = PlayerIcon(R.drawable.ic_notif_lyric_on)
    val SoundFx = PlayerIcon(R.drawable.ic_equalizer)
    val Timer = PlayerIcon(R.drawable.ic_timer)
    val ArrowBack = PlayerIcon(R.drawable.ic_arrow_back)
    val Close = PlayerIcon(R.drawable.ic_close)
}
