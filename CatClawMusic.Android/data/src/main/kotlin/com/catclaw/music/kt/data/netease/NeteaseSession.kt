package com.catclaw.music.kt.data.netease

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云登录态。
 *
 * 插件把 cookie 放在 `_cookie` 字段里由宿主注入；Android 端没有宿主，所以落到自己的
 * SharedPreferences。目前只承载「粘贴 cookie / 二维码登录回填」的会话字符串，
 * 账号名与 userId 一并缓存，用于「我的歌单」「红心」等需要身份的地方。
 */
@Singleton
class NeteaseSession @Inject constructor(
    @ApplicationContext context: Context,
) {
    private val prefs = context.getSharedPreferences("netease_session", Context.MODE_PRIVATE)

    var cookie: String?
        get() = prefs.getString(KEY_COOKIE, null)?.takeIf { it.isNotBlank() }
        set(value) {
            prefs.edit().putString(KEY_COOKIE, value).apply()
        }

    var userId: String?
        get() = prefs.getString(KEY_USER_ID, null)
        set(value) {
            prefs.edit().putString(KEY_USER_ID, value).apply()
        }

    var nickname: String?
        get() = prefs.getString(KEY_NICKNAME, null)
        set(value) {
            prefs.edit().putString(KEY_NICKNAME, value).apply()
        }

    val isLoggedIn: Boolean get() = !cookie.isNullOrBlank()

    fun clear() {
        prefs.edit().clear().apply()
    }

    private companion object {
        const val KEY_COOKIE = "cookie"
        const val KEY_USER_ID = "user_id"
        const val KEY_NICKNAME = "nickname"
    }
}
