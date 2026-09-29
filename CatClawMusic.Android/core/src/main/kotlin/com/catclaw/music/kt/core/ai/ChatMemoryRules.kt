package com.catclaw.music.kt.core.ai

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlin.math.max
import kotlin.math.min

/**
 * AI 长期记忆的规则 —— 对应 C# `ChatMemoryService` 里那套提炼/合并/渲染。
 *
 * 为什么单独一层：安卓侧 `AgentService` 早就有 `memoryProvider` 这个入口
 * （它会把内容包成一条 `[记忆]` 的 system 消息），但从来没人往里塞东西 ——
 * 于是"Yuki 记住了我的偏好"这句话在安卓端一直是空的，而编译、页面、测试全绿。
 * 把判据收在这里，才能在没有模型、没有磁盘的情况下把"什么会被记住、以什么顺序进 prompt"钉死。
 *
 * 类别标签（"偏好"、"用户画像"…）**留在代码里**而不是 strings.xml：
 * 它们是喂给模型的 prompt 的一部分，和提炼用的中文指令是一套的，
 * 翻成界面语言只会让模型收到的提示词与指令语言不一致。
 */
object ChatMemoryRules {

    /** 记忆总条数上限，超出时按重要性从低往高丢 */
    const val MAX_ITEMS = 50

    /** 攒够 `CHUNK_SIZE * 2` 条对话就触发一次提炼（C# 同值） */
    const val CHUNK_SIZE = 10

    /** 少于这么多条对话不值得问一次模型 */
    const val MIN_MESSAGES_TO_ANALYZE = 4

    /** 一次 prompt 里最多带多少条已有记忆（给模型看"已经记住了什么"，避免重复写） */
    const val PROMPT_ITEMS = 20

    const val DEFAULT_IMPORTANCE = 5
    const val MIN_IMPORTANCE = 1
    const val MAX_IMPORTANCE = 10

    /** 送进提炼窗口的对话条数上限 */
    const val CONVERSATION_WINDOW = CHUNK_SIZE * 2

    data class Item(
        val id: String,
        val category: String = CATEGORY_PREFERENCE,
        val content: String,
        val importance: Int = DEFAULT_IMPORTANCE,
        val createdAt: Long = 0L,
        val updatedAt: Long? = null,
    )

    const val CATEGORY_USER_PROFILE = "user_profile"
    const val CATEGORY_PREFERENCE = "preference"
    const val CATEGORY_PERSONAL = "personal"
    const val CATEGORY_HABIT = "habit"

    /** 该到"问一次模型、看有没有值得记的东西"的时机了吗 */
    fun shouldAnalyze(bufferedMessages: Int): Boolean = bufferedMessages >= CHUNK_SIZE * 2

    /** 提炼至少要凑够几句话，否则模型只会回一堆空数组，白花一次调用 */
    fun worthAnalyzing(bufferedMessages: Int): Boolean = bufferedMessages >= MIN_MESSAGES_TO_ANALYZE

    /**
     * 合并提炼结果：同 id 视为"更新已有记忆"（C# 让模型复用旧 id 就是这个意思），
     * 新 id 追加。空内容一律不收 —— 模型偶尔会回 `[{"content":""}]`。
     * 结果按重要性降序裁到 [MAX_ITEMS]，这样被丢掉的一定是最不重要的那条。
     */
    fun merge(existing: List<Item>, extracted: List<Item>, now: Long): List<Item> {
        val byId = LinkedHashMap<String, Item>()
        existing.forEach { byId[it.id] = it }
        extracted.forEach { incoming ->
            val content = incoming.content.trim()
            if (content.isEmpty()) return@forEach
            val current = byId[incoming.id]
            byId[incoming.id] = if (current == null) {
                incoming.copy(content = content, importance = clampImportance(incoming.importance), createdAt = now)
            } else {
                current.copy(
                    content = content,
                    category = incoming.category.ifBlank { current.category },
                    importance = clampImportance(incoming.importance),
                    updatedAt = now,
                )
            }
        }
        return byId.values.sortedWith(
            compareByDescending<Item> { it.importance }.thenByDescending { it.createdAt },
        ).take(MAX_ITEMS)
    }

    fun clampImportance(value: Int): Int = min(MAX_IMPORTANCE, max(MIN_IMPORTANCE, value))

    /**
     * 喂给模型的"已有记忆"清单。空表时给一句明确的"暂无记忆"而不是空字符串 ——
     * 空 prompt 段会让模型以为格式坏了，从而自由发挥。
     */
    fun renderMemoryForPrompt(items: List<Item>): String {
        if (items.isEmpty()) return "（暂无记忆）"
        return items.asSequence()
            .sortedByDescending { it.importance }
            .take(PROMPT_ITEMS)
            .joinToString("") { "- [${categoryLabel(it.category)}] ${it.content} (重要性:${it.importance})\n" }
    }

    /**
     * 注入给 `AgentService.memoryProvider` 的那段文本。
     *
     * 与 [renderMemoryForPrompt] 同一个格式：模型在对话里看到的"已有记忆"和它实际拿到的
     * 记忆必须是同一份措辞，否则它会以为那是别人家的数据。
     */
    fun renderAgentMemory(items: List<Item>): String = renderMemoryForPrompt(items)

    /** 对话转提炼输入。`role == "assistant"` 显示成 Yuki，其余都算用户说的 */
    fun renderConversation(messages: List<Pair<String, String>>): String =
        messages.takeLast(CONVERSATION_WINDOW)
            .joinToString("") { (role, content) ->
                val who = if (role == "assistant") "Yuki" else "用户"
                "$who: $content\n"
            }

    /** 模型该记的没记 / 记错了，都要能在设置页里看得见：这条给 UI 用 */
    fun categoryLabel(category: String): String = when (category) {
        CATEGORY_USER_PROFILE -> "用户画像"
        CATEGORY_PREFERENCE -> "偏好"
        CATEGORY_PERSONAL -> "个人情况"
        CATEGORY_HABIT -> "习惯"
        else -> "其他"
    }

    /** 空内容、纯空白都不配占一个名额；id 由调用方给（模型可能不给，这里兜底） */
    fun isRecordable(content: String): Boolean = content.isNotBlank()

    fun newId(existing: Set<String>, random: () -> Int): String {
        // 8 位十六进制，和 C# 的 `Guid.NewGuid().ToString("N")[..8]` 同形；
        // 撞上了就再抽一次 —— 50 条规模下几乎不会发生，但"几乎不会"不是"不会"
        var candidate = random().toUInt().toString(16).take(8).padStart(8, '0')
        while (existing.contains(candidate)) {
            candidate = random().toUInt().toString(16).take(8).padStart(8, '0')
        }
        return candidate
    }

    /**
     * 解析模型返回的提炼结果。
     *
     * 这是"外来输入"的边界，所以按最脏的情况写：模型会在 JSON 前后加解释文字、
     * 用 ```json 围栏包起来、把 importance 写成字符串、少给字段、多给字段。
     * 判据是 C# `ProcessExtractedMemoryAsync` 的那几条：取第一个 `[` 到最后一个 `]`，
     * 逐条宽松取值，**任何一条解析失败就丢掉那一条**，而不是让整批记忆陪葬。
     *
     * @param now 新条目的创建时间
     * @param nextId 造 id 的随机源（测试里给确定值）
     */
    fun parseExtracted(text: String, now: Long, nextId: () -> Int): List<Item> {
        val start = text.indexOf('[')
        val end = text.lastIndexOf(']')
        if (start < 0 || end <= start) return emptyList()
        val array = runCatching { Json.parseToJsonElement(text.substring(start, end + 1)).jsonArray }.getOrNull()
            ?: return emptyList()
        val taken = ArrayList<Item>(array.size)
        val used = HashSet<String>()
        array.forEach { element ->
            val obj = element as? JsonObject ?: return@forEach
            val content = obj.string("content")?.trim().orEmpty()
            if (content.isEmpty()) return@forEach
            val givenId = obj.string("id")?.trim().orEmpty()
            val id = givenId.ifEmpty { newId(used, nextId) }
            used += id
            taken += Item(
                id = id,
                category = obj.string("category")?.trim().orEmpty().ifEmpty { CATEGORY_PREFERENCE },
                content = content,
                importance = clampImportance(obj.number("importance") ?: DEFAULT_IMPORTANCE),
                createdAt = now,
            )
        }
        return taken
    }

    /** 值可能是 `"7"` 这种字符串形态，也可能真是数字 */
    private fun JsonObject.string(key: String): String? = (this[key] as? JsonPrimitive)
        ?.takeIf { it.content != "null" }?.content

    private fun JsonObject.number(key: String): Int? = string(key)?.trim()?.toIntOrNull()

    /**
     * 提炼用的系统提示词 —— 逐字照 C# `ChatMemoryService.AnalyzeAndExtractMemoryAsync`。
     *
     * 中文是刻意的：这段是要发给模型的指令，不是界面文案，所以不走 strings.xml；
     * 类别名（user_profile/preference/personal/habit）与 [categoryLabel] 是同一套，
     * 指令里说的类别和解析时认的类别必须对得上，改一处必须同时改另一处。
     */
    const val EXTRACTION_SYSTEM_PROMPT = """
你是一个用户画像分析助手。请从用户与AI助手的对话中提取值得长期记住的重要信息。

需要提取的信息类别：
1. user_profile - 用户基本信息（年龄、性别、职业、所在地等）
2. preference - 用户偏好（喜欢的音乐风格、歌手、歌曲类型等）
3. personal - 个人情况（心情、状态、生活事件等）
4. habit - 使用习惯（常用功能、操作偏好等）

规则：
- 只提取明确提到或强烈暗示的信息，不要猜测
- 如果没有值得记住的重要信息，返回空JSON数组
- 信息要简洁具体，每条10-30字
- importance: 1-10分，非常重要的给8-10分，一般偏好给5-7分，次要信息给1-4分
- 如果新信息与已有记忆重复或更新已有记忆，返回的条目id使用已有的id

返回严格的JSON数组格式，不要任何其他文字：
[{"id":"新生成8位id或已有id","category":"类别","content":"记忆内容","importance":数字}]
"""

    /**
     * 提炼的用户消息。带上"已有记忆"是为了让模型能复用 id 做更新而不是重复新增 ——
     * 少了这一段，同一句"我喜欢周杰伦"会被记成几十条。
     */
    fun extractionUserPrompt(memoryText: String, conversationText: String): String =
        "【已有记忆】\n$memoryText\n\n【最近对话】\n$conversationText\n\n请从以上对话中提取重要信息："
}
