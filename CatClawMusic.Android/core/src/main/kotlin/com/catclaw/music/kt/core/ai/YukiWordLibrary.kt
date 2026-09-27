package com.catclaw.music.kt.core.ai

import kotlin.random.Random

/** 一条词库条目（`wordlib.db` 的 WordEntry 表：Trigger → Reply） */
data class WordPair(val trigger: String, val reply: String)

/**
 * 词库查询缝。真实实现是 SQLite（`data/ai/SqliteWordSource.kt`），
 * 测试里给内存列表 —— 匹配规则（最长触发词优先、同长随机）是纯逻辑，不该依赖一个 1.8 MB 的 db。
 */
interface WordSource {
    /** 用户消息**包含**触发词的候选，按触发词长度降序，最多 [limit] 条 */
    fun matches(message: String, limit: Int): List<WordPair>

    /** 全库随机 [limit] 条 */
    fun random(limit: Int): List<WordPair>
}

/**
 * Yuki 人格词库，移植自 MAUI `Services/YukiWordLibrary.cs`。
 *
 * 两个用途，都接上了才不算死代码：
 * ① **未配置模型时**给一条有人格的本地回复（聊天页那条链路）；
 * ② **已配置模型时**抽若干条作为语气样本注入系统提示词。
 */
class YukiWordLibrary(
    private val source: WordSource?,
    private val random: Random = Random.Default,
) {

    /** 无词库、查不到、或查询抛异常都返回 null，让上层回落到"未配置"提示 */
    fun reply(userMessage: String): String? {
        val db = source ?: return null
        val msg = userMessage.trim()
        return try {
            val chosen = if (msg.isNotEmpty()) {
                val matches = db.matches(msg, MATCH_LIMIT)
                if (matches.isEmpty()) {
                    null
                } else {
                    // 最具体的触发词优先；同长度的多条里随机挑一条，避免每次回答一模一样
                    val maxLen = matches.maxOf { it.trigger.length }
                    matches.filter { it.trigger.length == maxLen }.let { best ->
                        best[random.nextInt(best.size)]
                    }
                }
            } else {
                null
            } ?: db.random(1).firstOrNull()
            chosen?.let { applyPlaceholders(it.reply) }
        } catch (error: Exception) {
            null
        }
    }

    /** 注入系统提示词的人格样本；词库不可用时返回空串 */
    fun knowledgePrompt(sampleSize: Int = DEFAULT_SAMPLE): String {
        val db = source ?: return ""
        return try {
            val sample = db.random(sampleSize)
            if (sample.isEmpty()) return ""
            buildString {
                appendLine("【Yuki 人格对话风格参考——请模仿这种可爱、傲娇、二次元的语气与用户交流】")
                sample.forEach {
                    append("用户：").appendLine(it.trigger)
                    append("Yuki：").appendLine(applyPlaceholders(it.reply))
                }
            }
        } catch (error: Exception) {
            ""
        }
    }

    companion object {
        const val MATCH_LIMIT = 50
        const val DEFAULT_SAMPLE = 30
        const val SELF_NAME = "yuki"
        const val USER_NAME = "你"

        /** `{me}`→自称、`{name}`→对用户的称呼，其余 `{...}`（如 `{segment}`）直接删掉 */
        fun applyPlaceholders(template: String): String =
            template.replace("{me}", SELF_NAME)
                .replace("{name}", USER_NAME)
                .replace(Regex("\\{[^}]*\\}"), "")
                .trim()
    }
}
