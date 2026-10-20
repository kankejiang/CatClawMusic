package com.catclaw.music.kt.core.ai

/**
 * 聊天历史翻页的判据。
 *
 * 抽出来是因为它以前肉眼看不出来错：那段代码挂在**消息数**上（`LaunchedEffect(messages.size)`），
 * 只有来新消息时才会顺带判一次「离最旧一条还有几条」。于是用户往上滚到顶、什么都不发生 ——
 * 得等 Yuki 回一句，更旧的历史才突然冒出来。改成滚动驱动之后，这个判断会在每次可见项变化时跑，
 * 所以它必须是个能单测的纯函数：边界差一条就是「翻页永远不触发」或「每帧都在翻」。
 */
object ChatPaging {

    /** 距离最旧一条还剩几条就开始预取 —— 留出这几条，滚到底之前下一页已经到位 */
    const val PREFETCH_THRESHOLD = 3

    /**
     * [lastVisibleIndex] 是最旧那一侧可见项的**下标**（列表是倒序：index 0 = 最新）。
     * 未布局时 `lastVisibleIndex` 为 -1（`visibleItemsInfo` 空），此时不该触发翻页。
     */
    fun shouldLoadOlder(
        lastVisibleIndex: Int,
        itemCount: Int,
        threshold: Int = PREFETCH_THRESHOLD,
    ): Boolean = itemCount > 0 && lastVisibleIndex >= 0 && lastVisibleIndex >= itemCount - threshold
}
