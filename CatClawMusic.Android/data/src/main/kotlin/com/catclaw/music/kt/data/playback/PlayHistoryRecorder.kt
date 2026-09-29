package com.catclaw.music.kt.data.playback

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.core.stats.PlayStatRules
import com.catclaw.music.kt.data.db.LibraryDao
import com.catclaw.music.kt.data.di.IoScope
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/**
 * 把"开始一次聆听"落成 `PlayHistory` 的一行，并开一条 `PlaySession` 会话行 + 三张汇总表。
 *
 * 为什么要有这个类：`PlayerController` 在 `:core`，那里没有数据库也不该有；
 * 而回调又是 fire-and-forget 的，不能让用户点下去等一次磁盘写。
 * 所以 :core 只发信号，落库、开 IO 协程都在这层。
 * 传进来的 [scope] 必须是**单 worker**的，理由见 `IoScopeModule`（并发的 record 会把会话 id 记错）。
 *
 * 这条链子此前是断的：`PlayHistory` 只有备份恢复会写，播放从来不记，
 * 于是「最近播放」和「常听排行」永远是空的 —— 而查询、页面、编译、测试全都是绿的。
 */
@Singleton
class PlayHistoryRecorder @Inject constructor(
    private val dao: LibraryDao,
    @IoScope private val scope: CoroutineScope,
) {
    /** 当前会话。四个字段一起变，所以统一在 [lock] 下读写 */
    private val lock = Any()
    private var sessionSongId = 0
    private var sessionId = 0L
    private var maxPositionMs = 0L
    private var samplesSinceFlush = 0

    /**
     * 一次聆听开始：刷新次数 + 建会话行（此刻时长 0，由 [sample] 累计、周期性落回同一行）。
     * 上一首还没结算的话，先在这里补上。
     */
    fun record(songId: Int) {
        if (songId <= 0) return
        AppLog.i(TAG, "record song=$songId")
        // 自动切歌时 isPlaying 一直是真，收尾采样拿到的已经是下一首 —— 上一首自上次落库后
        // 的那一段只能在这里补交，否则每首都会丢掉最多 30 秒
        val carry = synchronized(lock) { takePendingFlushLocked() }
        carry?.let { (id, ms) -> launchFlush(id, ms) }
        // 失败必须出声：这条链子整个是 fire-and-forget，静默吞掉就等于"最近播放"又是空的
        scope.launch {
            runCatching {
                val nowSeconds = System.currentTimeMillis() / 1000L
                dao.recordPlay(songId, nowSeconds)
                val keys = PlayStatRules.keysFor(nowSeconds, songId)
                val id = dao.logListenSession(songId, nowSeconds, keys)
                synchronized(lock) {
                    sessionSongId = songId
                    sessionId = id
                    maxPositionMs = 0L
                    samplesSinceFlush = 0
                }
            }.onFailure { AppLog.w(TAG, "record play $songId failed", it) }
        }
    }

    /**
     * 播放中的采样，只用来回答"这次听到了哪儿"。
     *
     * 取**进度条到过的位置**而不是墙钟差值：seek 到中段之后继续听，墙钟算出来的
     * 时长会比实际听到的少一个头；而进度位置天然是"听到的内容长度"。
     * 代价是拖回去重听的那段不计——统计口径上宁少不多。
     *
     * 会话**不因暂停而关闭**：[com.catclaw.music.kt.core.playback.ListenCounter] 按歌曲 id 去重，
     * 暂停后恢复同一首不会再触发 `record()`，若在这里清零就再也接不上了 —— 恢复之后听的
     * 那半首会整个丢掉。所以这里只"结算并写回同一行"，行与行的边界交给 `record()` 决定。
     */
    fun sample(songId: Int, positionMs: Long, playing: Boolean) {
        val due = synchronized(lock) {
            if (songId <= 0 || songId != sessionSongId || sessionId <= 0L) return
            if (positionMs > maxPositionMs) maxPositionMs = positionMs
            val paused = !playing
            samplesSinceFlush = if (paused) 0 else samplesSinceFlush + 1
            // 暂停时立刻结算：用户随时可能直接杀掉进程，停顿处是最自然的落库点
            if (!paused && samplesSinceFlush < FLUSH_EVERY_SAMPLES) return
            sessionId to maxPositionMs
        }
        launchFlush(due.first, due.second)
    }

    /** 交出并关闭当前会话（换歌时用）。调用方须已持有 [lock] */
    private fun takePendingFlushLocked(): Pair<Long, Long>? {
        if (sessionId <= 0L) return null
        val done = sessionId to maxPositionMs
        sessionId = 0L
        sessionSongId = 0
        maxPositionMs = 0L
        samplesSinceFlush = 0
        return done
    }

    private fun launchFlush(id: Long, ms: Long) {
        scope.launch {
            runCatching { dao.flushListenSession(id.toInt(), ms, System.currentTimeMillis() / 1000L) }
                .onFailure { AppLog.w(TAG, "flush session $id failed", it) }
        }
    }

    private companion object {
        const val TAG = "CatClawHistory"

        /**
         * 播放途中每隔这么多次采样落一次库（采样 1 秒一次，即约 30 秒）。
         * 目的是把"进程被杀丢多少收听时长"限死在半分钟内，同时**不新增行** ——
         * 汇总表按增量加，频率再高也不会把播放次数放大（C# 同口径）。
         */
        const val FLUSH_EVERY_SAMPLES = 30
    }
}
