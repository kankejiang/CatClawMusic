package com.catclaw.music.kt.lyrico.plugin

/**
 * 注入给 Lyrico 源插件的 JS 引导脚本。
 *
 * 职责：把扁平绑定的 `__lyricoHost.xxx(...)` 组装成插件期望的嵌套形态
 * `Platform.http.getText(...)` / `Platform.crypto.md5(...)` / …，
 * 并在跨界处统一做 JSON 编解码（宿主侧只认 String/Int）。
 *
 * 约定（与 Lyrico 官方宿主 API 一致）：
 *   - `Platform.http.get/post/postBytesResponse` 返回对象，宿主给的是 JSON 串，这里 parse
 *   - `Platform.cache.get` 未命中返回 undefined
 *   - `Platform.log.*` 支持 (message) 与 (tag, message) 两种调用
 *   - 字节数组在 JS 侧保持普通 Array<number>，跨界转成 JSON
 */
object LyricoBootstrap {

    val SCRIPT: String = """
    (function () {
      var H = __lyricoHost;

      function j(v) { return JSON.stringify(v === undefined ? null : v); }
      function p(s) { try { return JSON.parse(s); } catch (e) { return null; } }
      function opt(o) { return o === undefined || o === null ? "" : j(o); }

      var Platform = {
        http: {
          getText: function (url, options) { return H.httpGetText(url, opt(options)); },
          postText: function (url, body, options) {
            return H.httpPostText(url, typeof body === "string" ? body : j(body), opt(options));
          },
          get: function (url, options) { return p(H.httpGet(url, opt(options))); },
          post: function (url, body, options) {
            return p(H.httpPost(url, typeof body === "string" ? body : j(body), opt(options)));
          },
          postBytesResponse: function (url, body, options) {
            return p(H.httpPostBytesResponse(url, typeof body === "string" ? body : j(body), opt(options)));
          }
        },
        crypto: {
          md5: function (text) { return H.cryptoMd5(String(text)); },
          aesEcbPkcs5EncryptHex: function (text, key) { return H.cryptoAesEcbPkcs5EncryptHex(String(text), String(key)); },
          aesEcbPkcs5EncryptBase64: function (text, key) { return H.cryptoAesEcbPkcs5EncryptBase64(String(text), String(key)); },
          aesEcbPkcs5DecryptBase64ToText: function (b64, key) { return H.cryptoAesEcbPkcs5DecryptBase64ToText(String(b64), String(key)); }
        },
        base64: {
          encodeText: function (t) { return H.base64EncodeText(String(t)); },
          decodeText: function (b) { return H.base64DecodeText(String(b)); },
          encodeUrlText: function (t) { return H.base64EncodeUrlText(String(t)); },
          decodeUrlText: function (b) { return H.base64DecodeUrlText(String(b)); },
          dropBytes: function (b, n) { return H.base64DropBytes(String(b), Number(n) || 0); },
          encodeBytes: function (bytes) { return H.base64EncodeBytes(j(bytes)); },
          decodeBytes: function (b64) { return p(H.base64DecodeBytes(String(b64))) || []; }
        },
        bytes: {
          xorBase64: function (b64, keyBytes) { return H.bytesXorBase64(String(b64), j(keyBytes)); },
          xor: function (input, keyBytes) { return p(H.bytesXor(j(input), j(keyBytes))) || []; }
        },
        compression: {
          inflateBase64ToText: function (b64) { return H.compressionInflateBase64ToText(String(b64)); },
          inflateBytesToText: function (bytes) { return H.compressionInflateBytesToText(j(bytes)); }
        },
        cache: {
          get: function (key) {
            var r = p(H.cacheGet(String(key)));
            return r && r.hit ? r.value : undefined;
          },
          set: function (key, value, ttlMs) { H.cacheSet(String(key), String(value), Number(ttlMs) || 0); },
          remove: function (key) { H.cacheRemove(String(key)); },
          clear: function () { H.cacheClear(); }
        },
        log: {
          debug: function (a, b) { if (b === undefined) H.logDebug("Lyrico", String(a)); else H.logDebug(String(a), String(b)); },
          warn:  function (a, b) { if (b === undefined) H.logWarn("Lyrico", String(a));  else H.logWarn(String(a), String(b)); },
          error: function (a, b) { if (b === undefined) H.logError("Lyrico", String(a)); else H.logError(String(a), String(b)); }
        },
        runtime: {
          getInfo: function () { return p(H.runtimeGetInfo()); }
        },
        app: {
          getUserAgent: function () { return H.appGetUserAgent(); },
          getInfo: function () { return p(H.appGetInfo()); }
        },
        i18n: {
          // 插件用法：Platform.i18n.t("warn.x", a, b)
          // —— 可变参数；宿主侧按 printf 风格（百分号 1 美元符 s）填占位
          t: function (key) {
            var args = Array.prototype.slice.call(arguments, 1);
            return H.i18nT(String(key), args.length ? JSON.stringify(args) : "");
          }
        },
        xml: {
          getRootAttributes: function (xml) { return p(H.xmlGetRootAttributes(String(xml))) || {}; },
          findElements: function (xml, options) { return p(H.xmlFindElements(String(xml), opt(options))) || []; },
          removeElements: function (xml, options) { return H.xmlRemoveElements(String(xml), opt(options)); },
          replaceChildrenByAttr: function (xml, options) { return H.xmlReplaceChildrenByAttr(String(xml), opt(options)); }
        }
      };

      globalThis.Platform = Platform;
      globalThis.console = {
        log: function () {}, error: function () {}, warn: function () {},
        info: function () {}, debug: function () {}, trace: function () {}
      };
    })();
    """.trimIndent()

    /**
     * 调用插件函数并取回结果。
     *
     * 用「传入 JSON、返回 JSON」的方式跨语言，插件返回的对象/数组因此不会在
     * QuickJS 绑定的类型转换里丢结构。
     */
    val INVOKE_SCRIPT: String = """
    (function (fnName, requestJson) {
      try {
        var fn = globalThis[fnName];
        if (typeof fn !== "function") {
          return JSON.stringify({ ok: false, error: "missing function: " + fnName });
        }
        var out = fn(JSON.parse(requestJson));
        return JSON.stringify({ ok: true, value: out === undefined ? null : out });
      } catch (e) {
        return JSON.stringify({ ok: false, error: String((e && e.message) || e) });
      }
    })
    """.trimIndent()
}
