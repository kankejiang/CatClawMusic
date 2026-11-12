package com.catclaw.music.kt.data.repository.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.data.di.AppScope
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * 网易云「我喜欢的音乐」唯一状态源（对齐插件 `_likedIds`）。
 *
 * 用户明确要求「网易云是网易云、本地是本地」：这里只调网易云 API，
 * **不写宿主 Favorites 表**（插件原版还会 SyncFavoriteToDatabaseAsync，这一条不移植）。
 *
 * 登录态由 [NeteaseAccountRepository] 驱动：登录自动拉全量、登出自动清空；
 * 播放页 / 列表页 / 详情页读同一份 StateFlow，任意一处切换即时同步。
 */
@Singleton
class NeteaseLikeRepository @Inject constructor(
    private val api: NeteaseApiClient,
    private val account: NeteaseAccountRepository,
    @AppScope private val scope: CoroutineScope,
) {
    private val _likedIds = MutableStateFlow<Set<String>>(emptySet())
    val likedIds: StateFlow<Set<String>> = _likedIds.asStateFlow()

    private val _hint = MutableStateFlow<String?>(null)
    val hint: StateFlow<String?> = _hint.asStateFlow()

    private var loading = false
    private var loadedFor: Boolean? = null

    init {
        scope.launch {
            account.loggedIn.collect { logged ->
                if (logged) refresh(force = true) else clear()
            }
        }
    }

    fun refresh(force: Boolean = false) {
        val loggedIn = account.loggedIn.value
        if (!force && loadedFor == loggedIn) return
        if (loading) return
        loadedFor = loggedIn
        if (!loggedIn) {
            _likedIds.value = emptySet()
            return
        }
        loading = true
        scope.launch {
            val ids = runCatching { api.likedSongs().map { it.id }.toSet() }.getOrDefault(emptySet())
            _likedIds.value = ids
            AppLog.d("NeteaseLike", "refresh loggedIn=$loggedIn size=${ids.size}")
            loading = false
        }
    }

    /** 点红心：目标态由当前镜像决定，成功后立刻更新（所有页面同步） */
    fun toggle(songId: String) {
        if (!account.loggedIn.value) {
            _hint.value = "请先登录网易云账号"
            return
        }
        scope.launch {
            val target = songId !in _likedIds.value
            val ok = runCatching { api.likeSong(songId, target) }.getOrDefault(false)
            AppLog.d("NeteaseLike", "toggle id=$songId target=$target ok=$ok")
            if (ok) {
                _likedIds.value = if (target) _likedIds.value + songId else _likedIds.value - songId
                _hint.value = if (target) "已加入我喜欢的音乐" else "已取消红心"
            } else {
                _hint.value = "操作失败，请稍后再试"
            }
        }
    }

    /** 已经拉过完整「我喜欢的音乐」列表时直接采用，省一次重复请求 */
    fun adopt(ids: Set<String>) {
        _likedIds.value = ids
        loadedFor = account.loggedIn.value
    }

    fun clear() {
        _likedIds.value = emptySet()
        loadedFor = null
    }

    fun consumeHint() {
        _hint.value = null
    }
}
