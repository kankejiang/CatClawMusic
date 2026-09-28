package com.catclaw.music.kt.core.update

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * 版本比较与 GitHub Release 响应解析 —— 纯函数，`:core` 里可测。
 *
 * 语义照 C# `CatClawMusic.Maui/Services/UpdateService.cs`：
 * 只比**前三段**（`1.8.13.4` 与 `1.8.13` 视为相等），缺段与非数字段按 0 处理，
 * tag 前缀 `v` 先剥掉。这三条任何一条改动都会让"有没有新版本"的判断翻转，所以钉成测试。
 */
object UpdateRules {

    fun parseVersion(text: String?): IntArray {
        val parts = (text ?: "0.0.0").split('.')
        return IntArray(3) { i -> if (i < parts.size) parts[i].trim().toIntOrNull() ?: 0 else 0 }
    }

    /** 与 C# `CompareVersion` 同：>0 表示 a 比 b 新 */
    fun compareVersion(a: String?, b: String?): Int {
        val pa = parseVersion(a)
        val pb = parseVersion(b)
        for (i in 0..2) {
            if (pa[i] > pb[i]) return 1
            if (pa[i] < pb[i]) return -1
        }
        return 0
    }

    /**
     * 从 releases/latest 的响应里取最新版本号。
     * 没有 `tag_name`、空串、非 JSON 对象都回 null —— 调用方把 null 当"没有新版本"，
     * 不能回空串，否则界面上会显示"发现新版本 "这种半截话。
     */
    fun latestVersionFrom(jsonText: String?): String? {
        val root = jsonText?.let { runCatching { kotlinx.serialization.json.Json.parseToJsonElement(it) as? JsonObject }.getOrNull() }
            ?: return null
        val tag = (root["tag_name"] as? kotlinx.serialization.json.JsonPrimitive)
            ?.takeIf { it.isString }?.content ?: return null
        return tag.trim().removePrefix("v").takeIf { it.isNotBlank() }
    }
}
