package com.catclaw.music.kt.core.ai

import com.catclaw.music.kt.core.model.Song
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope

/** 一个可被模型调用的工具。[isReadOnly] 决定它能否与同轮的其他只读工具并行执行 */
interface AgentTool {
    val name: String
    val description: String
    val properties: Map<String, ToolParamSchema>
    val required: List<String>
    val isReadOnly: Boolean

    suspend fun execute(argumentsJson: String): String

    fun definition() = ToolDefinition(
        name = name,
        description = description,
        properties = properties,
        required = required,
    )
}

/**
 * Agent 主循环，逐条对齐 C# `AgentService.SendMessageAsync`（:393-644）。
 *
 * 三处行为是踩过坑才定下来的，别在"顺手优化"时改掉：
 * ① **死循环检测**：同一工具 + 完全相同参数连续 3 次就中断 —— 抓反爬页面反复失败这类
 *    无效重试会白烧轮次，最后报"步骤过多"却什么都没做；
 * ② **只读工具并行、写/控制类串行**：网络等待重叠提速，但状态变更必须保序；
 * ③ **tool 消息按调用顺序回填**，不是按完成顺序，否则模型看到的配对会错乱。
 */
class AgentService(
    /**
     * 注入成函数而不是具体客户端，才能用脚本化的假响应测循环本身（死循环、轮数上限、并行序）。
     * 必须是 suspend：C# 在 `ChatWithConfigAsync` 里专门注释过，Android 的 HttpClient 底层
     * HttpURLConnection 会在调用线程同步建连，从 UI 线程直接发请求会抛 NetworkOnMainThreadException。
     */
    private val chat: suspend (
        messages: List<ChatMessage>,
        tools: List<ToolDefinition>?,
        onDelta: ((LlmStreamDelta) -> Unit)?,
    ) -> LlmResponse,
    private val tools: List<AgentTool>,
    private val systemPrompt: () -> String,
    private val maxToolRounds: () -> Int = { AgentRunSettings.DEFAULT_MAX_TOOL_ROUNDS },
    private val personalityKnowledge: suspend () -> String = { "" },
    private val memoryProvider: () -> String = { "" },
    private val librarySnapshotProvider: () -> String = { "" },
    /** 未配置模型时的本地搜索（C# 直接调 IMusicLibraryService.SearchAsync） */
    private val searchSongs: suspend (keyword: String) -> List<Song> = { emptyList() },
    /** 当前音量百分比，本地音量命令要在它基础上 ±10 */
    private val volumePercent: () -> Int = { 100 },
    private val log: (String) -> Unit = {},
) {
    /**
     * 工具名查找表**忽略大小写**：模型偶发写出 `Web_Search` / `websearch` 这类变体，
     * 严格匹配会直接落到「未知工具」并白烧一轮（C# 用 StringComparer.OrdinalIgnoreCase 做同样的修复）。
     */
    private val toolMap: Map<String, AgentTool> =
        tools.associateBy({ it.name.lowercase() }, { it })
    private val toolDefs: List<ToolDefinition> = tools.map { it.definition() }

    val history = mutableListOf<ChatMessage>()

    fun clearConversation() = history.clear()

    data class Reply(val message: ChatMessage, val toolResults: List<ChatMessage>)

    suspend fun sendMessage(
        userMessage: String,
        onPartial: (ChatMessage) -> Unit = {},
    ): ChatMessage {
        if (history.isEmpty()) awaitFixedPrefix()
        history.add(ChatMessage(role = "user", content = userMessage))

        Conversation.trimConversation(history)

        val configured = maxToolRounds()
        val rounds = if (configured <= 0) Int.MAX_VALUE else configured
        val reasoning = StringBuilder()

        var lastToolKey: String? = null
        var lastToolRepeat = 0

        for (round in 0 until rounds) {
            val response = try {
                chat(Conversation.buildRequestMessages(history), toolDefs) { delta ->
                    if (delta.content.isEmpty() && delta.reasoningContent.isEmpty()) return@chat
                    onPartial(ChatMessage(role = "assistant", content = delta.content, reasoningContent = delta.reasoningContent))
                }
                    .also { log("LLM 响应 content='${it.content.take(200)}' toolCalls=${it.toolCalls.size} finish=${it.finishReason}") }
            } catch (cancelled: kotlinx.coroutines.CancellationException) {
                // 用户主动中断不算失败，原样上抛交给 UI
                throw cancelled
            } catch (error: Exception) {
                log("LLM 请求失败: ${error.message}")
                return addAssistant("抱歉，AI 服务请求失败：${error.message}")
            }

            if (response.reasoningContent.isNotEmpty()) {
                if (reasoning.isNotEmpty()) reasoning.append('\n')
                reasoning.append(response.reasoningContent)
            }

            if (response.toolCalls.isEmpty()) {
                return ChatMessage(
                    role = "assistant",
                    content = response.content,
                    reasoningContent = reasoning.toString().ifEmpty { null },
                ).also { history.add(it) }
            }

            val assistantMsg = ChatMessage(
                role = "assistant",
                content = response.content,
                toolCalls = response.toolCalls,
            )
            history.add(assistantMsg)
            onPartial(assistantMsg)

            // 只看本轮首个工具：与上一轮「名字 + 参数」完全一致就算一次重复
            val firstToolKey = "${response.toolCalls[0].name.lowercase()}|${response.toolCalls[0].arguments}"
            if (firstToolKey == lastToolKey) lastToolRepeat++ else {
                lastToolKey = firstToolKey
                lastToolRepeat = 1
            }
            if (lastToolRepeat >= DEAD_LOOP_REPEATS) {
                val name = response.toolCalls[0].name
                log("工具 $name 连续重复调用 $DEAD_LOOP_REPEATS 次，判定死循环，中断")
                return addAssistant(
                    "抱歉喵，$name 连续多次没有返回有效结果，我换个方式处理一下：${response.content.ifEmpty { "请稍后再试" }}",
                )
            }

            val results = executeTools(response.toolCalls)
            for (call in response.toolCalls) {
                val toolMsg = ChatMessage(
                    role = "tool",
                    content = results[call.id].orEmpty(),
                    toolCallId = call.id,
                )
                history.add(toolMsg)
                onPartial(toolMsg)
            }
        }

        return addAssistant("操作步骤过多，已停止执行。请尝试简化你的请求。")
    }

    /** 一次性快速问答：独立临时对话，不污染主历史，也不注入音乐库/记忆 */
    suspend fun quickAsk(systemPromptText: String, userPrompt: String): String =
        runCatching { chat(listOf(ChatMessage(role = "system", content = systemPromptText), ChatMessage(role = "user", content = userPrompt)), null, null).content }
            .getOrDefault("")

    /**
     * 固定前缀按「最稳定 → 最易变」排序，各自独立成 system 消息以提高服务端前缀缓存命中：
     * 系统提示 → 人格词库 → 记忆 → 音乐库。
     */
    private suspend fun awaitFixedPrefix() {
        history.add(ChatMessage(role = "system", content = systemPrompt()))

        runCatching {
            val personality = personalityKnowledge()
            if (personality.isNotEmpty()) {
                history.add(
                    ChatMessage(
                        role = "system",
                        content = if (personality.length > PERSONALITY_MAX) personality.take(PERSONALITY_MAX) + "\n.." else personality,
                    ),
                )
            }
        }

        runCatching {
            val memory = memoryProvider()
            if (memory.isNotEmpty()) {
                val recent = memory.lines().filter { it.isNotBlank() }.takeLast(MEMORY_LINES).joinToString("\n")
                history.add(
                    ChatMessage(
                        role = "system",
                        content = "[记忆]\n" + if (recent.length > MEMORY_MAX) recent.takeLast(MEMORY_MAX) else recent,
                    ),
                )
            }
            val library = librarySnapshotProvider()
            if (library.isNotEmpty()) {
                history.add(
                    ChatMessage(
                        role = "system",
                        content = "[音乐库]\n" + if (library.length > LIBRARY_MAX) library.take(LIBRARY_MAX) + ".." else library,
                    ),
                )
            }
        }
    }

    private suspend fun executeTools(calls: List<ToolCall>): Map<String, String> = coroutineScope {
        val out = java.util.concurrent.ConcurrentHashMap<String, String>()

        suspend fun run(call: ToolCall) {
            val tool = toolMap[call.name.lowercase()]
            if (tool == null) {
                log("调用了未知工具 ${call.name}")
                out[call.id] = """{"error":"未知工具: ${escape(call.name)}",""" +
                    """"hint":"可用工具: ${toolMap.keys.joinToString(", ")}。请使用正确的工具名重新调用"}"""
                return
            }
            // 分派前先校验必填项：缺参数的调用让工具自己跑只会抛一句模型看不懂的异常，
            // 白烧一轮；直接把 schema 回给它才有机会改对（C# 同一位置同一动机）
            val invalid = validateArguments(tool, call.arguments)
            if (invalid != null) {
                log("工具 ${tool.name} 参数校验失败")
                out[call.id] = invalid
                return
            }
            out[call.id] = runCatching { tool.execute(call.arguments) }
                .getOrElse { """{"error":"工具执行失败: ${escape(it.message.orEmpty())}"}""" }
                .let { truncateResult(it) }
        }

        calls.filter { toolMap[it.name]?.isReadOnly == true }
            .map { async { run(it) } }
            .awaitAll()
        calls.filterNot { toolMap[it.name]?.isReadOnly == true }.forEach { run(it) }

        out.toMap()
    }

    private fun addAssistant(content: String): ChatMessage =
        ChatMessage(role = "assistant", content = content).also { history.add(it) }

    /**
     * 不走模型的基础指令，对齐 C# `AgentService.TryLocalCommandAsync`（:75-164）：
     * 没配 API Key 时 Yuki 也得能暂停/切歌/报当前曲/调音量/搜歌。
     * 命中返回回复，未命中返回 null 交给上层（词库/未配置提示）。
     *
     * ⚠ 判定顺序照抄 C#：`播放` 先被当作 resume 消费掉，所以"播放"两个字不会走到下面的
     * 搜索分支（否则用户说"播放"会得到一列表格，而不是继续放当前歌）。
     */
    suspend fun tryLocalCommand(userMessage: String): ChatMessage? {
        val text = userMessage.trim()
        if (text.isEmpty()) return null

        val action = when {
            containsAny(text, "暂停") -> "pause"
            containsAny(text, "下一首", "下一曲", "切歌", "切到下一") -> "next"
            containsAny(text, "上一首", "上一曲", "切到上一") -> "previous"
            containsAny(text, "停止") -> "stop"
            containsAny(text, "继续", "恢复") ||
                text.equals("播放", ignoreCase = true) ||
                text.equals("resume", ignoreCase = true) ||
                text.equals("play", ignoreCase = true) -> "resume"
            else -> null
        }
        toolMap["control_playback"]?.let { control ->
            if (action != null) {
                val result = control.execute("""{"action":"$action"}""")
                return ChatMessage(role = "assistant", content = messageOf(result))
            }

            // ── 音量：静音 / 大点声 / 小点声（±10 个百分点，需读当前音量所以留在服务层）──
            // ⚠ current 必须在 execute 之前取：设完音量再读就是新值，"调小"会被判成"调大"
            val current = volumePercent()
            val muted = containsAny(text, "静音")
            val target = when {
                muted -> 0
                containsAny(text, "大点声", "大声", "音量调大", "音量加大", "调大音量", "声音大") ->
                    (current + 10).coerceIn(0, 100)
                containsAny(text, "小点声", "小声", "音量调小", "音量减小", "调小音量", "声音小") ->
                    (current - 10).coerceIn(0, 100)
                else -> null
            }
            if (target != null) {
                control.execute("""{"volume":$target}""")
                val reply = when {
                    muted -> "已静音"
                    target >= current -> "音量已调大到 $target"
                    else -> "音量已调小到 $target"
                }
                return ChatMessage(role = "assistant", content = reply)
            }
        }

        if (containsAny(text, "当前歌曲", "当前播放", "在放什么", "正在播放什么", "放的什么", "在听什么", "什么歌")) {
            toolMap["get_current_song"]?.let {
                return ChatMessage(role = "assistant", content = messageOf(it.execute("{}")))
            }
        }

        // ── 搜索：无模型也能搜，结果作为可点歌卡片 ──
        val keyword = extractSearchKeyword(text)
        if (keyword != null) {
            val songs = runCatching { searchSongs(keyword) }.getOrDefault(emptyList())
            if (songs.isNotEmpty()) {
                val top = songs.take(10)
                val more = if (songs.size > top.size) "，还有 ${songs.size - top.size} 首未显示" else ""
                return ChatMessage(
                    role = "assistant",
                    content = "找到 ${songs.size} 首和「$keyword」相关的歌曲喵$more，点一下就能播放~",
                    songs = top,
                )
            }
        }

        return null
    }

    private companion object {
        const val DEAD_LOOP_REPEATS = 3
        const val PERSONALITY_MAX = 2000
        const val MEMORY_LINES = 15
        const val MEMORY_MAX = 300
        const val LIBRARY_MAX = 600
        const val RESULT_MAX_LINES = 400
        const val RESULT_MAX_BYTES = 24_000

        /** 搜索动词按长度降序，先匹长的（"搜一下" 不该被 "搜" 截断成剩余 "一下"） */
        val SEARCH_VERBS = listOf(
            "搜一下", "查找", "找一下", "我想听", "来一首", "帮我搜", "帮我找", "搜索", "播放", "搜", "找", "放", "听",
        )
        val SEARCH_QUALIFIERS = listOf("歌曲", "音乐", "专辑", "歌手", "一下", "歌")

        fun containsAny(text: String, vararg keywords: String) = keywords.any { text.contains(it, ignoreCase = true) }

        /** 取最靠前的搜索动词之后的内容，剔掉限定词；无搜索意图返回 null */
        fun extractSearchKeyword(text: String): String? {
            var bestIdx = Int.MAX_VALUE
            var bestEnd = -1
            for (verb in SEARCH_VERBS) {
                val idx = text.indexOf(verb, ignoreCase = true)
                if (idx in 0 until bestIdx) {
                    bestIdx = idx
                    bestEnd = idx + verb.length
                }
            }
            if (bestEnd < 0) return null
            var keyword = text.substring(bestEnd).trim()
            for (q in SEARCH_QUALIFIERS) keyword = keyword.replace(q, "")
            return keyword.trim().takeIf { it.isNotEmpty() }
        }

        private val JSON = kotlinx.serialization.json.Json { ignoreUnknownKeys = true }

        /** 工具返回的是 JSON，取其中的 message 字段作为人话；解析失败原样返回 */
        fun messageOf(result: String): String = runCatching {
            val obj = JSON.parseToJsonElement(result) as? kotlinx.serialization.json.JsonObject
            (obj?.get("message") as? kotlinx.serialization.json.JsonPrimitive)?.content
        }.getOrNull() ?: result

        fun escape(value: String): String = value.replace("\\", "\\\\").replace("\"", "\\\"")

        /** 行数 + 字节双限，保留头部并打截断标记（对齐 C# `AgentToolDispatch.TruncateResult`） */
        fun truncateResult(value: String): String {
            val lines = value.lines()
            if (lines.size <= RESULT_MAX_LINES && value.length <= RESULT_MAX_BYTES) return value
            val kept = lines.take(RESULT_MAX_LINES).joinToString("\n")
            val clipped = if (kept.length > RESULT_MAX_BYTES) kept.substring(0, RESULT_MAX_BYTES) else kept
            return clipped + "\n…[结果已截断]"
        }
    }
}
