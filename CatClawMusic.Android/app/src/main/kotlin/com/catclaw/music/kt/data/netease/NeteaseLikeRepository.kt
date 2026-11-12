package com.catclaw.music.kt.data.netease

import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteaseSession
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 网易云「我喜欢的音乐」状态中心。
 *
 * 与插件 `_likedIds` 同一职责：红心的**事实在服务器**，本地只留一份可观察镜像。
 * 用户明确要求「网易云是网易云、本地是本地」：这里只调网易云 API，
 * **不写宿主 Favorites 表**（插件原版还会 SyncFavoriteToDatabaseAsync，这一条不移植）。
 *
 * 做成单例是因为红心状态要跨页面同步：播放页、网易云列表页、歌单详情读的是同一份 StateFlow，
 * 任意一处切换，其它处立刻跟着变。
 */
@Singleton
class NeteaseLikeRepository @Inject constructor(
    private val api: NeteaseApiClient,
    private val session: NeteaseSession,
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    private val _likedIds = MutableStateFlow<Set<String>>(emptySet())
    val likedIds: StateFlow<Set<String>> = _likedIds.asStateFlow()

    private val _hint = MutableStateFlow<String?>(null)
    val hint: StateFlow<String?> = _hint.asStateFlow()

    private var loading = false
    private var loadedFor: Boolean? = null

    /** 登录态变化或首次进入时拉一次「我喜欢的音乐」全量 id 集合 */
    fun refresh(force: Boolean = false) {
        val loggedIn = session.isLoggedIn
        if (!force && loadedFor == loggedIn) return
        if (loading) return
        loadedFor = loggedIn
        loading = true
        scope.launch {
            val ids = if (loggedIn) {
                runCatching { api.likedSongs().map { it.id }.toSet() }.getOrDefault(emptySet())
            } else {
                emptySet()
            }
            _likedIds.value = ids
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseLike", "refresh loggedIn=$loggedIn size=${ids.size}",
            )
            loading = false
        }
    }

    /** 点红心：目标态由当前镜像决定，成功后立刻更新镜像（所有页面同步） */
    fun toggle(songId: String) {
        if (!session.isLoggedIn) {
            _hint.value = "请先登录网易云账号"
            return
        }
        scope.launch {
            val target = songId !in _likedIds.value
            val ok = runCatching { api.likeSong(songId, target) }.getOrDefault(false)
            com.catclaw.music.kt.core.diagnostic.AppLog.d(
                "NeteaseLike", "toggle id=$songId target=$target ok=$ok",
            )
            if (ok) {
                _likedIds.value = if (target) _likedIds.value + songId else _likedIds.value - songId
                com.catclaw.music.kt.core.diagnostic.AppLog.d(
                    "NeteaseLike", "镜像已更新 size=${_likedIds.value.size} contains=$target",
                )
                _hint.value = if (target) "已加入我喜欢的音乐" else "已取消红心"
            } else {
                _hint.value = "操作失败，请稍后再试"
            }
        }
    }

    /** 已经拉过完整「我喜欢的音乐」列表时直接采用，省一次重复请求 */
    fun adopt(ids: Set<String>) {
        _likedIds.value = ids
        loadedFor = session.isLoggedIn
    }

    fun clear() {
        _likedIds.value = emptySet()
        loadedFor = null
    }

    fun consumeHint() {
        _hint.value = null
    }
}
