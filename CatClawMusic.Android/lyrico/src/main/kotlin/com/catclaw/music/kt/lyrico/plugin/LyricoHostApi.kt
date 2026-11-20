package com.catclaw.music.kt.lyrico.plugin

/**
 * 暴露给 JS 的宿主能力（`globalThis.__lyricoHost`）。
 *
 * ## 设计约定
 *
 * **所有参数与返回值只用 String / Int / Unit**，结构化数据以 JSON 字符串传递。
 * 原因见 `LyricoJsEngine` 的说明：cashapp 的 QuickJS 绑定对 JS 对象/数组没有稳定的
 * 结构化映射，用 JSON 串做边界最稳，也让这些方法能直接被单测覆盖。
 *
 * 方法名与 Lyrico 官方宿主 API 一一对应，由 `LyricoBootstrap` 里的 JS 代码
 * 组装成插件期望的 `Platform.http.getText(...)` 这类调用形态。
 */
interface LyricoHostApi {

    // ── http ────────────────────────────────────────────────────────────
    /** @return 响应正文（UTF-8） */
    fun httpGetText(url: String, optionsJson: String): String
    fun httpPostText(url: String, body: String, optionsJson: String): String

    /** @return 响应对象 JSON：{code,message,headers,body,bodyBase64} */
    fun httpGet(url: String, optionsJson: String): String
    fun httpPost(url: String, body: String, optionsJson: String): String
    fun httpPostBytesResponse(url: String, body: String, optionsJson: String): String

    // ── crypto ──────────────────────────────────────────────────────────
    fun cryptoMd5(text: String): String
    fun cryptoAesEcbPkcs5EncryptHex(text: String, key: String): String
    fun cryptoAesEcbPkcs5EncryptBase64(text: String, key: String): String
    fun cryptoAesEcbPkcs5DecryptBase64ToText(base64: String, key: String): String

    // ── base64 ──────────────────────────────────────────────────────────
    fun base64EncodeText(text: String): String
    fun base64DecodeText(base64: String): String
    fun base64EncodeUrlText(text: String): String
    fun base64DecodeUrlText(base64Url: String): String
    fun base64DropBytes(base64: String, count: Int): String
    /** @param bytesJson JS 字节数组的 JSON（[1,2,3]） */
    fun base64EncodeBytes(bytesJson: String): String
    /** @return JS 字节数组的 JSON */
    fun base64DecodeBytes(base64: String): String

    // ── bytes ───────────────────────────────────────────────────────────
    fun bytesXorBase64(base64: String, keyBytesJson: String): String
    fun bytesXor(inputBytesJson: String, keyBytesJson: String): String

    // ── compression ─────────────────────────────────────────────────────
    fun compressionInflateBase64ToText(base64: String): String
    fun compressionInflateBytesToText(bytesJson: String): String

    // ── cache ───────────────────────────────────────────────────────────
    /** @return JSON：{"hit":true,"value":"..."} */
    fun cacheGet(key: String): String
    fun cacheSet(key: String, value: String, ttlMs: Double)
    fun cacheRemove(key: String)
    fun cacheClear()

    // ── log ─────────────────────────────────────────────────────────────
    fun logDebug(tag: String, message: String)
    fun logWarn(tag: String, message: String)
    fun logError(tag: String, message: String)

    // ── runtime / app ───────────────────────────────────────────────────
    /** @return JSON：{pluginApiVersion,hostApiVersion,engine,engineVersion,supportedHostApis} */
    fun runtimeGetInfo(): String
    fun appGetUserAgent(): String
    /** @return JSON：{name,versionName} */
    fun appGetInfo(): String

    // ── i18n ────────────────────────────────────────────────────────────
    /** @param argsJson 命名参数 JSON（可空串） */
    fun i18nT(key: String, argsJson: String): String

    // ── xml ─────────────────────────────────────────────────────────────
    fun xmlGetRootAttributes(xml: String): String
    fun xmlFindElements(xml: String, optionsJson: String): String
    fun xmlRemoveElements(xml: String, optionsJson: String): String
    fun xmlReplaceChildrenByAttr(xml: String, optionsJson: String): String
}
