package com.catclaw.music.kt.core.ai

import com.catclaw.music.kt.core.model.Song
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.withContext

/** 一条已落库的聊天消息 */
data class StoredMessage(val id: Int, val role: String, val content: String)

/**
 * 一条气泡。[id] 为负数表示尚未落库的本会话消息（数据库自增 Id 从 1 开始，不会撞）。
 *
 * ⚠ [messages] 是**新→旧倒序**（index 0 = 最新），直接喂给
 * `LazyColumn(reverseLayout = true)` 后 index 0 落在视觉底部，与 MAUI
 * `SearchPage.xaml:498+` 那个 `Rotation="180"` 的 CollectionView 同构。
 * 选倒序不是为了复刻，而是它让「向上翻更旧的」变成**尾部追加**：
 * 已有项的下标不动，就不需要在插入后修滚动位置（正序列表每次加载更多都要重锚一次）。
 */
data class Bubble(
    val id: Int,
    val role: String,
    val content: String = "",
    val reasoning: String = "",
    val steps: List<String> = emptyList(),
    val thinking: Boolean = false,
    val songs: List<Song> = emptyList(),
)

/**
 * 聊天会话状态机，逐条对齐 MAUI `SearchViewModel.Chat.cs`。
 *
 * 逻辑（分页、占位气泡、流式增量、中断保留半句）与 Android 无关，所以放在 `:core`
 * 并用函数缝注入：真机不点也能在 JVM 上跑完全部路径。
 */
class ChatSession(
    private val agentSend: suspend (text: String, onPartial: (ChatMessage) -> Unit) -> ChatMessage,
    private val localCommand: suspend (text: String) -> ChatMessage? = { null },
    private val isConfigured: () -> Boolean = { true },
    private val persist: suspend (role: String, content: String) -> Unit = { _, _ -> },
    private val loadRecent: suspend (limit: Int) -> List<StoredMessage> = { emptyList() },
    private val loadOlder: suspend (beforeId: Int, limit: Int) -> List<StoredMessage> = { _, _ -> emptyList() },
) {
    private val localIds = AtomicInteger(0)

    private val _messages = MutableStateFlow<List<Bubble>>(emptyList())
    val messages: StateFlow<List<Bubble>> = _messages.asStateFlow()

    private val _thinking = MutableStateFlow(false)
    val thinking: StateFlow<Boolean> = _thinking.asStateFlow()

    var oldestLoadedId = 0
        private set
    var hasMoreHistory = false
        private set
    private var loadingMore = false
    private var loaded = false

    /** 进入聊天模式：首次加载最近一页历史。重复调用只在真空列表时补一次 */
    suspend fun open() {
        if (loaded) return
        loaded = true
        val records = runCatching { loadRecent(RECENT_PAGE) }.getOrDefault(emptyList())
        if (records.isEmpty()) {
            _messages.value = listOf(
                Bubble(
                    id = localIds.decrementAndGet(),
                    role = "assistant",
                    content = if (isConfigured()) GREETING else GREETING_UNCONFIGURED,
                ),
            )
            oldestLoadedId = 0
            hasMoreHistory = false
            return
        }
        // 存储层返回旧→新；反转成 index 0 = 最新
        _messages.value = records.reversed().map { Bubble(it.id, it.role, it.content) }
        oldestLoadedId = records.first().id
        // 取满一页即认为还有更多，省掉一次全表 COUNT（与 MAUI 同一取舍）
        hasMoreHistory = records.size == RECENT_PAGE
    }

    /** 翻到列表末端时加载更旧的一页，追加到尾部（视觉上出现在上方） */
    suspend fun loadMore() {
        if (loadingMore || !hasMoreHistory || oldestLoadedId <= 0) return
        loadingMore = true
        try {
            val older = runCatching { loadOlder(oldestLoadedId, OLDER_PAGE) }.getOrDefault(emptyList())
            if (older.isEmpty()) {
                hasMoreHistory = false
                return
            }
            _messages.update { it + older.reversed().map { r -> Bubble(r.id, r.role, r.content) } }
            oldestLoadedId = older.first().id
            hasMoreHistory = older.size == OLDER_PAGE
        } finally {
            loadingMore = false
        }
    }

    /** [text] 由 UI 侧持有（Compose `rememberSaveable`），会话层不复制一份输入框状态 */
    suspend fun send(text: String) {
        val userMessage = text.trim()
        if (userMessage.isEmpty()) return
        loaded = true

        append(Bubble(localIds.decrementAndGet(), "user", content = userMessage))
        persistSafe("user", userMessage)

        if (!isConfigured()) {
            val reply = runCatching { localCommand(userMessage) }.getOrNull()
            val content = reply?.content?.takeIf { it.isNotBlank() } ?: NOT_CONFIGURED_HINT
            append(Bubble(localIds.decrementAndGet(), "assistant", content = content, songs = reply?.songs.orEmpty()))
            persistSafe("assistant", content)
            return
        }

        val id = localIds.decrementAndGet()
        append(Bubble(id, "assistant", thinking = true, steps = listOf(THINKING_STEP)))
        _thinking.value = true
        val toolNames = HashMap<String, String>()

        try {
            val response = agentSend(userMessage) { partial -> onPartial(id, partial, toolNames) }
            replace(id) { b ->
                b.copy(
                    content = buildAssistantMessage(response),
                    reasoning = response.reasoningContent?.takeIf { it.isNotEmpty() } ?: b.reasoning,
                    songs = response.songs,
                    thinking = false,
                    steps = b.steps.dropThinkingPlaceholder(),
                )
            }
            persistSafe("assistant", currentContent(id, buildAssistantMessage(response)))
        } catch (cancelled: CancellationException) {
            // 用户点「停止」：半句也是有效回复，保留并标注（MAUI 同一处理）
            replace(id) { b ->
                b.copy(
                    content = b.content.ifBlank { "已停止回复喵。" } + if (b.content.isBlank()) "" else "\n\n（已停止回复）",
                    thinking = false,
                    steps = b.steps.dropThinkingPlaceholder(),
                )
            }
            withContext(NonCancellable) { persistSafe("assistant", currentContent(id, "已停止回复喵。")) }
        } catch (error: Exception) {
            replace(id) { b ->
                b.copy(
                    content = "出错了喵：${error.message.orEmpty()}",
                    thinking = false,
                    steps = b.steps.dropThinkingPlaceholder(),
                )
            }
            persistSafe("assistant", currentContent(id, "出错了喵。"))
        } finally {
            _thinking.value = false
        }
    }

    /** Agent 中段回调：工具步骤进折叠区，正文/推理增量进气泡本身 */
    private fun onPartial(id: Int, partial: ChatMessage, toolNames: MutableMap<String, String>) {
        if (partial.role == "assistant" && !partial.toolCalls.isNullOrEmpty()) {
            partial.toolCalls.forEach { toolNames[it.id] = it.name }
            val names = partial.toolCalls.joinToString(", ") { it.name.ifEmpty { "?" } }
            _messages.update { list ->
                list.replaceBubble(id) { b -> b.copy(steps = (b.steps + "🔧 调用工具: $names").dropThinkingPlaceholder()) }
            }
            return
        }
        if (partial.role == "tool") {
            val name = toolNames[partial.toolCallId] ?: return
            _messages.update { list -> list.replaceBubble(id) { b -> b.copy(steps = b.steps + "✅ $name 完成") } }
            return
        }
        val reasoningDelta = partial.reasoningContent.orEmpty()
        if (partial.role == "assistant" && reasoningDelta.isNotEmpty()) {
            _messages.update { list ->
                list.replaceBubble(id) { b ->
                    b.copy(reasoning = b.reasoning + reasoningDelta, steps = b.steps.dropThinkingPlaceholder())
                }
            }
            return
        }
        if (partial.role == "assistant" && partial.content.isNotEmpty()) {
            val delta = partial.content
            _messages.update { list -> list.replaceBubble(id) { b -> b.copy(content = b.content + delta) } }
        }
    }

    private fun append(bubble: Bubble) {
        _messages.update { listOf(bubble) + it }
    }

    private fun replace(id: Int, transform: (Bubble) -> Bubble) {
        _messages.update { it.replaceBubble(id, transform) }
    }

    private fun currentContent(id: Int, fallback: String): String =
        _messages.value.firstOrNull { it.id == id }?.content ?: fallback

    private suspend fun persistSafe(role: String, content: String) {
        runCatching { persist(role, content) }
    }

    private companion object {
        const val RECENT_PAGE = 30
        const val OLDER_PAGE = 20
        const val THINKING_STEP = "💭 正在思考你的问题..."
        const val GREETING = "Yuki 在这里喵，可以帮你找歌、放歌、建歌单。"
        const val GREETING_UNCONFIGURED = "Yuki 在这里喵，不过 AI 还没配置，先去设置页完成配置吧。"
        const val NOT_CONFIGURED_HINT =
            "AI 还没有配置好喵，先到“设置 > AI 设置”里填一下模型信息吧。\n\n" +
                "不过基础的播放控制我可以直接帮你：暂停 / 播放 / 下一首 / 上一首 / 停止 / 当前歌曲 / 调音量。"

        fun buildAssistantMessage(response: ChatMessage): String = when {
            response.content.isNotBlank() -> response.content
            response.songs.isNotEmpty() -> "帮你找到 ${response.songs.size} 首相关歌曲喵。"
            else -> "处理完成喵。"
        }

        fun List<String>.dropThinkingPlaceholder(): List<String> =
            if (firstOrNull()?.startsWith("💭") == true) drop(1) else this

        fun List<Bubble>.replaceBubble(id: Int, transform: (Bubble) -> Bubble): List<Bubble> =
            map { if (it.id == id) transform(it) else it }
    }
}
