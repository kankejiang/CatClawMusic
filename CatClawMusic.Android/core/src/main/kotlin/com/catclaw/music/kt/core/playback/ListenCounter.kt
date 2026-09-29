package com.catclaw.music.kt.core.playback

/**
 * 「一次聆听」的判定。
 *
 * 抽成纯函数是因为这条规则的两个失败方向都很安静：
 * - 挂在"每次 publish"上 → 暂停/恢复/缓冲都会各记一次，常听排行几天就虚高到没法看
 *   （C# 侧真出过这个事故，`RecalibratePlayCountsAsync` 就是事后校正用的）；
 * - 只在用户点击处记 → 自动切歌、通知栏下一曲这些路径全部漏记。
 *
 * 所以判定只看状态迁移：**正在播的那首歌变了** 才算一次。
 * 与 C# 的一处偏离：C# 在专辑/艺人详情页每次点击都记一次，
 * 本端把"同一首歌反复点"算成一次聆听的一部分，避免"连点三下=播放三次"这种明显失真。
 */
class ListenCounter {

    private var lastSongId = 0

    /**
     * @param songId 当前歌曲 id（队列为空时传 0）
     * @param playing 是否真的在播（暂停/缓冲都不算开始一次聆听）
     * @return 需要记录聆听的歌曲 id；null 表示这次状态不算一次新聆听
     */
    fun onPublish(songId: Int, playing: Boolean): Int? {
        if (!playing || songId <= 0) return null
        if (songId == lastSongId) return null
        lastSongId = songId
        return songId
    }

    /** 队列清空时调用：否则"播完 A → 清空 → 再播 A"不会被算成两次 */
    fun onQueueCleared() {
        lastSongId = 0
    }
}
