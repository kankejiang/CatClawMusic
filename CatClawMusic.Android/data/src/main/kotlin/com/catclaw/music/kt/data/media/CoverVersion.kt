package com.catclaw.music.kt.data.media

import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 「封面可能变了」的全局版本号。
 *
 * ## 为什么需要它
 *
 * 封面是**异步补出来的**：播放某首远程曲时后台从标签抽图落盘，然后把路径写回
 * `Songs.CoverArtPath`。但 UI 读封面并不读库 —— `rememberCover(coverArtPath, audioPath)`
 * 以这两个字符串为 key，而它们来自**播放队列里的 Song 快照**（`controller.queueSongs`）
 * 或**列表项快照**。补全只改了数据库，这两处的 `coverArtPath` 一个字都没变，
 * `produceState` 的 key 不变就不会重新执行 —— 表现就是「图已经存好了，界面还是首字母，
 * 得杀掉重进才显示」。
 *
 * ## 为什么不直接把新值推进队列
 *
 * 播放队列存的是 `Song`（core 模型），而封面补全发生在 `:data`（写 Room）。让补全器
 * 反向去改播放队列会形成 `:data → :core 状态` 的隐藏耦合，而且列表页（Room Flow 驱动）
 * 与队列页（播放器快照驱动）是两条不同的数据链，漏掉任何一条就还是"有的页面刷新、有的不刷新"。
 *
 * 所以只广播一个**版本号**：谁显示封面，谁把它当 key 的一部分。版本变了，key 就变了，
 * `produceState` 自然重跑，重新查库/重新读文件。这与 `NeteaseLikeRepository` 用单例
 * StateFlow 让多页同步是同一个模式。
 *
 * 代价是全屏播放页那种大图会重解一次（有内存 LRU 兜着，同一张图不会真重复解码）。
 * 这比「补完不刷新」好得多，而且只在真的写出新封面时才递增。
 */
@Singleton
class CoverVersion @Inject constructor() {

    private val _version = MutableStateFlow(0)
    val version: StateFlow<Int> = _version.asStateFlow()

    /** 有封面写入（或删除）后调用。必须在**落盘并写库之后**，否则 UI 会先读到旧图 */
    fun bump() {
        _version.value += 1
    }
}
