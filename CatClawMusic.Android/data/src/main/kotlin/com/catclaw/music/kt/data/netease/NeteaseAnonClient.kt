package com.catclaw.music.kt.data.netease

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import java.security.MessageDigest
import java.util.Base64
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * 网易云游客账号注册 —— 移植 api-enhanced `module/register_anonimous.js`。
 *
 * 原理：把设备指纹按云音乐桌面 DLL 的算法编码（逐字符 XOR 固定盐 → MD5 → Base64），
 * 以 `username` 提交 `/api/register/anonimous`，服务端下发**游客账号会话**（MUSIC_A）。
 * 携带 MUSIC_A 的匿名请求按"游客账号"处理而非纯匿名设备 —— 非 VIP 歌完整可播、
 * eapi 风控显著降低（api-enhanced 启动时注册并把 MUSIC_A 缓存进所有匿名请求，同机制）。
 *
 * 与 api-enhanced 的差异：注册请求走 **weapi** 而不是 xeapi —— 原版 NeteaseCloudMusicApi
 * 就用 weapi 且长期有效，xeapi 需要 RSA 公钥协商那套重通道，为这一个接口不值得引入。
 * token 持久化在 [NeteaseSession]，进程内并发注册用 [Mutex] 收敛成一次。
 */
@Singleton
class NeteaseAnonClient @Inject constructor(
    @ApplicationContext context: Context,
    private val weapi: NeteaseWeapi,
    private val session: NeteaseSession,
) {

    private val mutex = Mutex()

    /** 游客设备指纹：52 位大写 hex（api-enhanced `generateDeviceId` 同格式），跨进程稳定 */
    private val prefs = context.getSharedPreferences("netease_anon", Context.MODE_PRIVATE)

    private val deviceId: String by lazy {
        prefs.getString(KEY_DEVICE_ID, null) ?: generateDeviceId().also {
            prefs.edit().putString(KEY_DEVICE_ID, it).apply()
        }
    }

    /**
     * 确保游客令牌可用；已注册直接返回 true，未注册时注册一次（并发收敛）。
     * 失败返回 false（调用方按"无游客身份"继续走，不阻塞播放）。
     */
    suspend fun ensureToken(): Boolean {
        if (session.anonymousToken != null) return true
        return mutex.withLock {
            if (session.anonymousToken != null) return@withLock true
            register()
        }
    }

    private suspend fun register(): Boolean {
        val result = weapi.call(
            path = "/api/register/anonimous",
            body = mapOf("username" to encodedUsername()),
        )
        val cookie = result.cookie
        if (result.code != 200 || cookie.isNullOrBlank()) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w(
                "NeteaseAnon", "register/anonimous 失败 code=${result.code} ${result.message}",
            )
            return false
        }
        val musicA = cookie.split(';')
            .mapNotNull { part ->
                val kv = part.trim().split('=', limit = 2)
                if (kv.size == 2 && kv[0].trim() == "MUSIC_A") kv[1].trim() else null
            }
            .firstOrNull()
        if (musicA.isNullOrBlank()) {
            com.catclaw.music.kt.core.diagnostic.AppLog.w("NeteaseAnon", "响应无 MUSIC_A cookie")
            return false
        }
        session.anonymousToken = musicA
        com.catclaw.music.kt.core.diagnostic.AppLog.d("NeteaseAnon", "游客令牌已注册 len=${musicA.length}")
        return true
    }

    /** `username = base64("<deviceId> <xor-md5-base64(deviceId)>")`（云音乐 DLL 编码算法） */
    private fun encodedUsername(): String {
        val encoded = cloudmusicDllEncodeId(deviceId)
        val plain = "$deviceId $encoded"
        return Base64.getEncoder().encodeToString(plain.toByteArray(Charsets.UTF_8))
    }

    /** 逐字符 XOR `3go8&$8*3*3h0k(2)2`（循环取模）→ MD5 → Base64 */
    private fun cloudmusicDllEncodeId(id: String): String {
        val xored = buildString(id.length) {
            for (i in id.indices) {
                append((id[i].code xor ID_XOR_KEY_1[i % ID_XOR_KEY_1.length].code).toChar())
            }
        }
        val digest = MessageDigest.getInstance("MD5").digest(xored.toByteArray(Charsets.UTF_8))
        return Base64.getEncoder().encodeToString(digest)
    }

    private fun generateDeviceId(): String = buildString(52) {
        repeat(52) { append(HEX[Random.nextInt(HEX.length)]) }
    }

    private companion object {
        const val KEY_DEVICE_ID = "device_id"
        const val ID_XOR_KEY_1 = "3go8&$8*3*3h0k(2)2"
        const val HEX = "0123456789ABCDEF"
    }
}
