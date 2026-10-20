package com.catclaw.music.kt.ui.lyrics

/**
 * 非当前行的雾化模糊半径（dp），**按离当前行的行数递进**。
 *
 * 2026-10-19 用户要求：「非当前行歌词加一个雾化模糊效果，离当前行越远效果越强」。
 * 原来的实现是两档（±1 行 1dp、≥2 行一律 2dp 封顶），两行开外就一视同仁，
 * 而且当时为了做性能剖析被 `if (false)` 直通掉了（`FullLyricsScreen.kt:613` 的 TEMP-PROFILE），
 * 所以真机上一直看不到任何雾化。
 *
 * 渐变曲线：每多隔一行 +0.9dp，6dp 封顶 ——
 * 相邻行只是轻轻失焦（还能读出字），隔得越远越化开，视线自然回落到当前行。
 * 0 行（当前行）恒为 0：当前行是唯一要清晰可读、并且要逐字着色的那行。
 */
internal fun lyricBlurDp(distance: Int): Float {
    if (distance <= 0) return 0f
    return (distance * 0.9f).coerceAtMost(6f)
}
