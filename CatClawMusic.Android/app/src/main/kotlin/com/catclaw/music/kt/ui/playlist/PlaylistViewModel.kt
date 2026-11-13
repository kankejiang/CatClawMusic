package com.catclaw.music.kt.ui.playlist

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.data.db.PlaylistCover
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import com.catclaw.music.kt.data.repository.netease.NeteaseAccountRepository
import com.catclaw.music.kt.data.repository.playlist.PlaylistRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/**
 * 「我的歌单」列表页 ViewModel：只负责歌单集合的增删改/排序与网易云货架。
 *
 * 详情页（打开某个歌单后的状态）已拆到 [PlaylistDetailViewModel]，
 * 数据统一来自 [PlaylistRepository]。
 */
@HiltViewModel
class PlaylistViewModel @Inject constructor(
    private val repo: PlaylistRepository,
    private val accountRepo: NeteaseAccountRepository,
) : ViewModel() {

    /**
     * 网易云账号里的歌单（含「我喜欢的音乐」）：唯一源在 NeteaseAccountRepository，
     * 登录动作发生在网易云页，回到歌单页时调 [refreshNetease] 触发一次共享刷新。
     */
    val neteaseAccount: StateFlow<NeteaseApiClient.Account?> = accountRepo.account
    val neteasePlaylists: StateFlow<List<OnlinePlaylist>> = accountRepo.myPlaylists

    val playlists = repo.playlists.stateIn(
        scope = viewModelScope,
        started = SharingStarted.WhileSubscribed(5_000),
        initialValue = emptyList(),
    )

    /**
     * 歌单封面来源表（SQL 见 `PlaylistDao.SQL_PLAYLIST_COVERS`）。
     * 没曲的歌单不在表里，卡片自然退回首字 / 系统图标。
     */
    val covers: StateFlow<Map<Int, PlaylistCover>> = repo.covers
        .map { rows -> rows.associateBy { it.playlistId } }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyMap())

    fun refreshNetease() {
        accountRepo.refresh(force = true)
    }

    fun create(name: String) {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return
        viewModelScope.launch {
            if (repo.byName(trimmed) == null) repo.create(trimmed)
        }
    }

    /**
     * 重命名。空名与「别的歌单已占用这个名字」直接拒，不发 SQL。
     * @return false 表示没改，UI 应当保留编辑框并说明原因
     */
    fun rename(id: Int, name: String): Boolean {
        val trimmed = name.trim()
        if (trimmed.isEmpty()) return false
        if (playlists.value.any { it.id != id && it.name == trimmed }) return false
        viewModelScope.launch { repo.rename(id, trimmed) }
        return true
    }

    /** 内置歌单（收藏 / 最近播放）删不掉：DAO 的 DELETE 带 IsSystem = 0 */
    fun delete(id: Int) {
        viewModelScope.launch { repo.delete(id) }
    }

    /**
     * 提交拖拽后的歌单顺序：只提交自建歌单（系统歌单恒在最前）。
     * 落库失败只影响下次进页的顺序，不影响本次会话显示。
     */
    fun commitPlaylistOrder(orderedIds: List<Int>) {
        val ids = orderedIds.filter { id -> playlists.value.any { it.id == id && !it.isSystem } }
        if (ids.isEmpty()) return
        viewModelScope.launch { repo.reorder(ids) }
    }
}
