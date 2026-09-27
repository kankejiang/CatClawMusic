package com.catclaw.music.kt.data.lyrics

import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * 桌面歌词悬浮窗**当前真的在不在屏幕上**。
 *
 * 为什么要单独一个类而不是让 UI 直接读 `DesktopLyricController.visible`：
 * 控制器构造时会创建 TextView/FrameLayout，把它注进 ViewModel 等于让 UI 层去拉起一个视图对象。
 * 这个状态位是纯数据，谁都能读。
 *
 * 存在的理由是一个真机现象：`settings.enabled` 记的是**用户意图**，而窗口的生死跟着
 * `MediaPlaybackService` 走 —— 服务被系统回收或还没起来时，意图仍是"开"，屏幕上却什么都没有。
 * 标签只读意图就会撒谎。
 */
@Singleton
class DesktopLyricStatus @Inject constructor() {
    private val _attached = MutableStateFlow(false)
    val attached: StateFlow<Boolean> = _attached.asStateFlow()

    fun setAttached(value: Boolean) {
        if (_attached.value != value) _attached.value = value
    }
}
