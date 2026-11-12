package com.catclaw.music.kt.data.repository.netease

import com.catclaw.music.kt.core.diagnostic.AppLog
import com.catclaw.music.kt.data.di.AppScope
import com.catclaw.music.kt.data.netease.NeteaseApiClient
import com.catclaw.music.kt.data.netease.NeteaseLoginClient
import com.catclaw.music.kt.data.netease.NeteaseSession
import com.catclaw.music.kt.data.netease.OnlinePlaylist
import javax.inject.Inject
import javax.inject.Singleton
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.launch

/**
 * 网易云账号唯一状态源（登录态 / 用户信息 / 我的歌单）。
 *
 * 旧实现里 `NeteaseMusicViewModel`、`PlaylistViewModel`、`NeteaseRadioRepository` 各存一份登录态，
 * 两个页面各自请求 `account()` + `userPlaylists()`。这里收敛成单例：
 * 凭据真源仍是 [NeteaseSession]（SharedPreferences），身份与歌单由本类派生并缓存。
 * 登录 / 续期 / 登出都会让 `session.revision` 自增，本类据此自动刷新。
 */
@Singleton
class NeteaseAccountRepository @Inject constructor(
    private val api: NeteaseApiClient,
    private val loginClient: NeteaseLoginClient,
    private val session: NeteaseSession,
    @AppScope private val scope: CoroutineScope,
) {
    private val _loggedIn = MutableStateFlow(session.isLoggedIn)
    val loggedIn: StateFlow<Boolean> = _loggedIn.asStateFlow()

    private val _account = MutableStateFlow<NeteaseApiClient.Account?>(null)
    val account: StateFlow<NeteaseApiClient.Account?> = _account.asStateFlow()

    private val _myPlaylists = MutableStateFlow<List<OnlinePlaylist>>(emptyList())
    val myPlaylists: StateFlow<List<OnlinePlaylist>> = _myPlaylists.asStateFlow()

    private var refreshing = false

    init {
        // 会话版本变化（登录/续期/登出）→ 重新拉身份
        scope.launch {
            session.revision.drop(1).collect { refresh(force = true) }
        }
        scope.launch { refresh() }
    }

    /** 刷新身份与歌单；未登录时直接清空，不发请求 */
    fun refresh(force: Boolean = false) {
        val logged = session.isLoggedIn
        _loggedIn.value = logged
        if (!logged) {
            _account.value = null
            _myPlaylists.value = emptyList()
            return
        }
        if (refreshing && !force) return
        refreshing = true
        scope.launch {
            runCatching {
                _account.value = api.account()
                _myPlaylists.value = api.userPlaylists()
                AppLog.d(
                    "NeteaseAccount",
                    "refresh account=${_account.value?.nickname} playlists=${_myPlaylists.value.size}",
                )
            }
            refreshing = false
        }
    }

    suspend fun sendSms(phone: String, countryCode: String = "86"): NeteaseLoginClient.Outcome =
        loginClient.sendSmsCode(phone, countryCode)

    suspend fun loginWithSms(
        phone: String,
        captcha: String,
        countryCode: String = "86",
    ): NeteaseLoginClient.Outcome = loginClient.loginWithSms(phone, captcha, countryCode)

    suspend fun loginWithPassword(
        account: String,
        password: String,
        countryCode: String = "86",
    ): NeteaseLoginClient.Outcome = loginClient.loginWithPassword(account, password, countryCode)

    fun logout() {
        loginClient.logout()
        _loggedIn.value = false
        _account.value = null
        _myPlaylists.value = emptyList()
    }
}
