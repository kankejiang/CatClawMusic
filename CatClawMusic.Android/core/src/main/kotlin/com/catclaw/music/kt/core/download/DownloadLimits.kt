package com.catclaw.music.kt.core.download

/**
 * 下载限速的算术 —— 对应 C# `DownloadManager.MaxDownloadBytesPerSecond`（`SetSpeedLimitKbps`）。
 *
 * 单独抽出来是因为它有两个容易写错、又很难在真机上发现的点：
 * 1. `0 = 不限` 而不是「限 0」—— 后者会把下载彻底卡死，而界面上两档只差一个数字；
 * 2. 限速要按**本次会话**写入的字节算，不能按文件总进度算：续传时拿含上次已下部分的 `written`
 *    去算，第一次循环就会 sleep 出几十秒，表现成「点了续传就不动」。
 */
object DownloadLimits {

    /** 限速档位（KB/s），0 = 不限。档位是本端自定的（C# 那边是一个滑杆） */
    val SPEED_STEPS_KBPS = listOf(0, 256, 512, 1024, 2048, 4096)

    /** KB/s → 字节/秒。0 与负数都表示不限（返回 0） */
    fun speedBytesPerSecond(kbps: Int): Long = if (kbps > 0) kbps * 1024L else 0L

    /**
     * 还该等多少毫秒（0 = 不用等）。
     *
     * 按累计值算而不是逐块各睡一次：块大小由网络决定（可能一次 1KB、也可能 80KB），
     * 逐块算会让实际均速随块大小漂移。
     *
     * @param sessionBytes 本次会话写入的字节数（**不含**续传时上次留下的部分）
     * @param elapsedMs 本次会话已花掉的时间
     */
    fun throttleDelayMs(sessionBytes: Long, elapsedMs: Long, bytesPerSecond: Long): Long {
        if (bytesPerSecond <= 0) return 0
        val expected = sessionBytes * 1000 / bytesPerSecond
        return (expected - elapsedMs).coerceAtLeast(0)
    }
}
