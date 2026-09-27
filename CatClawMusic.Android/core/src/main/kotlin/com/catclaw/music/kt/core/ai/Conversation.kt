package com.catclaw.music.kt.core.ai

/**
 * 对话历史的分段裁剪与配对修复，逐条对齐 C# `AgentService` 的
 * `BuildSegmentBounds`（:711-748）/ `SanitizeToolMessagePairs`（:751-804）/ `TrimConversationHistory`（:820-845）。
 *
 * 为什么必须按「段」而不是按「条」裁：带 `tool_calls` 的 assistant 与它对应的 tool 消息是一对，
 * 拆开后 OpenAI 兼容端点会直接 400，报
 * `tool must be a response to a preceding message with tool_calls`
 * 或 `insufficient tool messages following tool_calls message`。
 * 这是 C# 注释里标了「关键修复」的地方 —— 按条裁在短对话上看不出问题，跑到工具轮次多了才炸。
 */
object Conversation {

    /** 历史条数超过这个值才裁 */
    const val TRIM_THRESHOLD = 24

    /** 裁完保留的非 system 段数上限（约 6~12 轮工具循环） */
    const val KEEP_SEGMENTS = 12

    /** 发给 API 前的 token 预算；Agent 工具循环会累积大量 tool 消息，所以给到 32000 */
    const val TOKEN_BUDGET = 32_000

    /** 请求级裁剪时至少保留的消息数，低于它就不再裁 */
    const val MIN_MESSAGES = 3

    /**
     * 段边界（闭区间）。规则：
     * ① 每条 system 单独成段，绝不与非 system 一起被裁；
     * ② 带 tool_calls 的 assistant + 紧随其后且 id 属于它的那批 tool 消息合成一段；
     * ③ user / 普通 assistant / 其他独立消息各自一段。
     */
    fun segmentBounds(messages: List<ChatMessage>): List<IntRange> {
        val bounds = ArrayList<IntRange>(messages.size)
        var i = 0
        while (i < messages.size) {
            val m = messages[i]
            if (m.role == "system") {
                bounds.add(i..i)
                i++
                continue
            }
            val calls = m.toolCalls
            if (m.role == "assistant" && !calls.isNullOrEmpty()) {
                val ids = calls.map { it.id }.toHashSet()
                var end = i
                var j = i + 1
                while (j < messages.size && messages[j].role == "tool" &&
                    !messages[j].toolCallId.isNullOrEmpty() && ids.contains(messages[j].toolCallId)
                ) {
                    end = j
                    j++
                }
                bounds.add(i..end)
                i = end + 1
                continue
            }
            bounds.add(i..i)
            i++
        }
        return bounds
    }

    /**
     * 修复配对一致性，原地改。两遍都**从后往前**删，避免删完索引错位。
     * ① tool 消息：往前的最近一条非 tool 消息必须是带对应 id 的 assistant，否则删掉这条 tool；
     * ② assistant(tool_calls)：后面紧挨的 tool 必须把每个 id 都应答齐，缺任何一个就把
     *    这条 assistant 连同已挂在它后面的孤立 tool 一起删掉。
     */
    fun sanitizeToolMessagePairs(messages: MutableList<ChatMessage>) {
        for (i in messages.lastIndex downTo 0) {
            val m = messages[i]
            if (m.role != "tool" || m.toolCallId.isNullOrEmpty()) continue
            var matched = false
            var j = i - 1
            while (j >= 0) {
                val prev = messages[j]
                if (prev.role == "tool") { j--; continue }
                if (prev.role == "assistant" && prev.toolCalls?.any { it.id == m.toolCallId } == true) {
                    matched = true
                }
                break // 只看最近一条非 tool 的相邻消息
            }
            if (!matched) messages.removeAt(i)
        }

        for (i in messages.lastIndex downTo 0) {
            val m = messages[i]
            val calls = m.toolCalls
            if (m.role != "assistant" || calls.isNullOrEmpty()) continue
            val expected = calls.map { it.id }.toHashSet()
            val got = HashSet<String>()
            var j = i + 1
            while (j < messages.size && messages[j].role == "tool") {
                messages[j].toolCallId?.let { if (expected.contains(it)) got.add(it) }
                j++
            }
            if (expected.any { it !in got }) {
                for (k in (j - 1) downTo i) messages.removeAt(k)
            }
        }
    }

    /**
     * 历史裁剪：保留全部 system + 最近 KEEP_SEGMENTS 段，整段删所以不会拆散配对。
     * 裁完再兜一次 [sanitizeToolMessagePairs]。
     */
    fun trimConversation(history: MutableList<ChatMessage>) {
        if (history.size <= TRIM_THRESHOLD) return
        val systemMsgs = history.filter { it.role == "system" }
        val nonSystem = history.filter { it.role != "system" }
        val bounds = segmentBounds(nonSystem)
        val targetSegments = minOf(KEEP_SEGMENTS, bounds.size)
        val keepFrom = (bounds.size - targetSegments).coerceAtLeast(0)
        val startIdx = if (keepFrom < bounds.size) bounds[keepFrom].first else 0
        val recent = if (nonSystem.size <= startIdx) emptyList() else nonSystem.subList(startIdx, nonSystem.size)

        history.clear()
        history.addAll(systemMsgs)
        history.addAll(recent)
        sanitizeToolMessagePairs(history)
    }

    /** C# 的估算式，一个系数都不能改：每条 6、正文 len/2、每个调用 80 + 参数 len/2，末尾再 +1200 */
    fun estimateTokens(messages: List<ChatMessage>): Int {
        var total = 0
        for (m in messages) {
            total += 6
            if (m.content.isNotEmpty()) total += m.content.length / 2
            m.toolCalls?.forEach { tc ->
                total += 80
                if (tc.arguments.isNotEmpty()) total += tc.arguments.length / 2
            }
        }
        return total + 1200
    }

    /**
     * 发给 API 前的请求级裁剪：在 [estimateTokens] 超预算时，从头删第一个非 system 段，
     * 直到不超或不足 [MIN_MESSAGES] 条。返回的是**副本**，不动真实历史。
     */
    fun buildRequestMessages(history: List<ChatMessage>): List<ChatMessage> {
        val messages = history.toMutableList()
        var bounds = segmentBounds(messages)
        while (messages.size > MIN_MESSAGES && estimateTokens(messages) > TOKEN_BUDGET) {
            val victim = bounds.firstOrNull { messages[it.first].role != "system" } ?: break
            for (i in victim.last downTo victim.first) messages.removeAt(i)
            bounds = segmentBounds(messages)
        }
        sanitizeToolMessagePairs(messages)
        return messages
    }
}
