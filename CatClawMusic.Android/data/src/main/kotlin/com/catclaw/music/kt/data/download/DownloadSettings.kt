package com.catclaw.music.kt.data.download

import android.content.Context
import com.catclaw.music.kt.core.download.DownloadGate
import com.catclaw.music.kt.core.download.DownloadLimits
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 下载相关的用户偏好（限速）。对应 C# `DownloadManager` 里那两个 Preferences 键。
 *
 * 存的是**档位值（KB/s）**而不是换算后的字节数：界面要显示人话，换算只在取用时做一次，
 * 两边各存一份迟早会出现「界面写 512、实际按 500 跑」。
 */
@Singleton
class DownloadSettings @Inject constructor(
    @ApplicationContext context: Context,
) {

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** 每任务限速（KB/s），0 = 不限 */
    var speedKbps: Int
        get() = prefs.getInt(KEY_SPEED, 0).let { if (it in DownloadLimits.SPEED_STEPS_KBPS) it else 0 }
        set(value) {
            // 只接受档位内的值：写进一个 10000 也能跑，但界面那排档位就再也对不上了
            val safe = if (value in DownloadLimits.SPEED_STEPS_KBPS) value else 0
            prefs.edit().putInt(KEY_SPEED, safe).apply()
        }

    /** 给下载器用的字节/秒（0 = 不限） */
    fun speedBytesPerSecond(): Long = DownloadLimits.speedBytesPerSecond(speedKbps)

    /** 点一下切到下一档；到头回到「不限」 */
    fun nextSpeedKbps(): Int {
        val steps = DownloadLimits.SPEED_STEPS_KBPS
        val at = steps.indexOf(speedKbps)
        speedKbps = steps[(at + 1).mod(steps.size)]
        return speedKbps
    }

    /** 并发上限，默认与 C# `DefaultConcurrent` 同值（2），夹取 1..5 */
    var concurrent: Int
        get() = DownloadGate.clampConcurrent(prefs.getInt(KEY_CONCURRENT, DownloadGate.DEFAULT_CONCURRENT))
        set(value) {
            prefs.edit().putInt(KEY_CONCURRENT, DownloadGate.clampConcurrent(value)).apply()
        }

    /** 点一下把并发调到下一档；到顶回到 1 */
    fun nextConcurrent(): Int {
        val next = concurrent + 1
        concurrent = if (next > DownloadGate.MAX_CONCURRENT) DownloadGate.MIN_CONCURRENT else next
        return concurrent
    }

    private companion object {
        const val PREFS = "catclaw_state"
        const val KEY_SPEED = "download_speed_kbps"
        const val KEY_CONCURRENT = "download_concurrent"
    }
}
