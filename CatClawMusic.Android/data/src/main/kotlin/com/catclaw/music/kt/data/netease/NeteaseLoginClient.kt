package com.catclaw.music.kt.data.netease

import javax.inject.Inject
import javax.inject.Singleton

/**
 * 网易云登录 —— 移植插件 `NeteaseAccountLoginPage` 背后的三条流程。
 *
 * 全部走 weapi（`/weapi/…`），因为会话（MUSIC_U）在响应的 Set-Cookie 里；
 * 手机号验证码流程按官方顺序「先 verify 再 login」，这样验证码错了能给出明确提示，
 * 而不是让用户看着"登录失败"猜是号码还是验证码的问题。
 */
@Singleton
class NeteaseLoginClient @Inject constructor(
    private val weapi: NeteaseWeapi,
    private val eapi: NeteaseEapi,
    private val session: NeteaseSession,
) {

    data class Outcome(
        val ok: Boolean,
        val code: Int,
        val message: String?,
    )

    /** 发送短信验证码。返回 code=200 表示已下发 */
    suspend fun sendSmsCode(phone: String, countryCode: String = "86"): Outcome {
        val p = phone.trim()
        if (p.length < 6) return Outcome(false, -1, "请填写正确的手机号")
        val result = weapi.call(
            "/api/sms/captcha/sent",
            mapOf(
                "ctcode" to country(countryCode),
                "secrete" to "music_middleuser_pclogin",
                "cellphone" to p,
            ),
        )
        return Outcome(result.code == 200, result.code, result.message)
    }

    /** 验证码登录：verify → login，两步都在这里，UI 只处理结果 */
    suspend fun loginWithSms(phone: String, captcha: String, countryCode: String = "86"): Outcome {
        val p = phone.trim()
        val c = captcha.trim()
        if (p.length < 6) return Outcome(false, -1, "请填写正确的手机号")
        if (c.isEmpty()) return Outcome(false, -1, "请填写短信验证码")

        val verify = weapi.call(
            "/api/sms/captcha/verify",
            mapOf("ctcode" to country(countryCode), "cellphone" to p, "captcha" to c),
        )
        if (verify.code != 200) {
            return Outcome(false, verify.code, verify.message ?: "验证码校验失败")
        }

        val login = weapi.call(
            "/api/w/login/cellphone",
            mapOf(
                "type" to "1",
                "https" to "true",
                "phone" to p,
                "countrycode" to country(countryCode),
                "captcha" to c,
                "remember" to "true",
                "secureCaptcha" to "",
            ),
        )
        return finish(login, "手机号验证码登录")
    }

    /**
     * 账号密码登录。
     * - 纯数字按手机号走 `/api/w/login/cellphone`（type=1）；
     * - 含 `@` 按邮箱走 `/api/w/login`（type=0）。
     * 密码必须是 MD5 小写十六进制（与插件一致），不是明文。
     */
    suspend fun loginWithPassword(account: String, password: String, countryCode: String = "86"): Outcome {
        val acc = account.trim()
        if (acc.isEmpty() || password.isEmpty()) return Outcome(false, -1, "请填写账号和密码")
        val md5 = NeteaseWeapi.md5HexLower(password)
        val isPhone = acc.all { it.isDigit() }

        val result = if (isPhone) {
            weapi.call(
                "/api/w/login/cellphone",
                mapOf(
                    "type" to "1",
                    "https" to "true",
                    "phone" to acc,
                    "countrycode" to country(countryCode),
                    "password" to md5,
                    "remember" to "true",
                    "secureCaptcha" to "",
                ),
            )
        } else {
            weapi.call(
                "/api/w/login",
                mapOf(
                    "type" to "0",
                    "https" to "true",
                    "username" to acc,
                    "password" to md5,
                    "rememberLogin" to "true",
                ),
            )
        }
        return finish(result, "账号密码登录")
    }

    /**
     * 会话续期：`/eapi/login/token/refresh`。
     *
     * 与插件一致：`code=200` 即成功，服务端可能不轮换 MUSIC_U（手机号验证码会话就是这样，
     * 有效期已顺延）；只有在 Set-Cookie 里给了新会话时才需要写回。
     */
    suspend fun refreshSession(): Boolean {
        val cookie = session.cookie ?: return false
        val result = runCatching {
            eapi.requestWithCookies("/eapi/login/token/refresh", emptyMap(), true, cookie)
        }.getOrNull() ?: return false
        val code = result.body?.let {
            runCatching {
                kotlinx.serialization.json.Json { ignoreUnknownKeys = true }
                    .parseToJsonElement(it)
                    .let { element ->
                        (element as? kotlinx.serialization.json.JsonObject)
                            ?.get("code")?.let { c ->
                                (c as? kotlinx.serialization.json.JsonPrimitive)?.content?.toIntOrNull()
                            }
                    }
            }.getOrNull()
        }
        if (code == 200) {
            session.mergeCookies(result.cookies)
            return true
        }
        return false
    }

    fun logout() {
        session.clear()
    }

    private fun finish(result: NeteaseWeapi.Result, what: String): Outcome {
        val cookie = result.cookie
        if (result.code == 200 && !cookie.isNullOrBlank()) {
            session.cookie = cookie
            return Outcome(true, 200, null)
        }
        return Outcome(
            ok = false,
            code = result.code,
            message = result.message ?: when (result.code) {
                501 -> "账号不存在"
                502 -> "账号或密码错误"
                503 -> "操作过于频繁，请稍后再试"
                8810, 8821 -> "触发安全验证，请稍后重试或改用验证码登录"
                else -> "$what 失败（code=${result.code}）"
            },
        )
    }

    private fun country(raw: String): String = raw.trim().ifEmpty { "86" }
}
