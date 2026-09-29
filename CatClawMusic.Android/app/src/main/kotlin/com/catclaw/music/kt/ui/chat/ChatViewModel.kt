package com.catclaw.music.kt.ui.chat

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.catclaw.music.kt.core.ai.AgentService
import com.catclaw.music.kt.core.ai.ChatSession
import com.catclaw.music.kt.core.ai.StoredMessage
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.core.playback.PlayerController
import com.catclaw.music.kt.data.ai.ChatMemoryStore
import com.catclaw.music.kt.data.ai.LlmConfigStore
import com.catclaw.music.kt.data.db.ChatDao
import com.catclaw.music.kt.data.db.entity.ChatMessageEntity
import com.catclaw.music.kt.data.service.PlaybackSessionStarter
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

/**
 * Yuki 聊天页的宿主，对应 MAUI `SearchViewModel` 的聊天域分部。
 * 会话逻辑本身在 `:core` 的 [ChatSession]（可 JVM 测），这里只做依赖装配与协程生命周期。
 */
@HiltViewModel
class ChatViewModel @Inject constructor(
    private val agent: AgentService,
    private val chatDao: ChatDao,
    private val configStore: LlmConfigStore,
    private val player: PlayerController,
    private val session: PlaybackSessionStarter,
    private val words: com.catclaw.music.kt.core.ai.YukiWordLibrary,
    private val memory: ChatMemoryStore,
) : ViewModel() {

    val chat = ChatSession(
        agentSend = { text, onPartial -> agent.sendMessage(text, onPartial) },
        localCommand = { text ->
            // 顺序照 MAUI：基础播放指令 → 人格词库回复 → 都没有才落到"未配置"提示
            agent.tryLocalCommand(text) ?: words.reply(text)?.let {
                com.catclaw.music.kt.core.ai.ChatMessage(role = "assistant", content = it)
            }
        },
        isConfigured = configStore::isConfigured,
        persist = { role, content ->
            chatDao.insert(ChatMessageEntity(role = role, content = content, timestamp = System.currentTimeMillis()))
            // 与 MAUI 同一取舍：每存一条顺手查一次总量，超 1000 才裁
            if (chatDao.count() > CHAT_TRIM_KEEP) chatDao.trimKeepingNewest(CHAT_TRIM_KEEP)
            // 存消息的这条缝同时是"喂长期记忆"的唯一入口：用户和 Yuki 说的每句话都经过这里。
            // 攒够一批就后台问一次模型"有什么值得记的"；没配置模型时不问，但把缓冲丢掉 ——
            // 留着的话，用户哪天配好模型会突然被翻旧账。
            if (memory.record(role, content)) {
                if (configStore.isConfigured()) {
                    viewModelScope.launch { memory.extractWith(agent::quickAsk) }
                } else {
                    memory.dropPending()
                }
            }
        },
        loadRecent = { limit -> chatDao.recent(limit).map { StoredMessage(it.id, it.role, it.content) } },
        loadOlder = { beforeId, limit ->
            chatDao.olderThan(beforeId, limit).map { StoredMessage(it.id, it.role, it.content) }
        },
    )

    val messages = chat.messages
    val thinking = chat.thinking

    /** 顶栏标题：当前生效的模型配置名（MAUI 的 `AgentName` 同源） */
    val title = configStore.currentName

    private var sendJob: Job? = null

    fun open() {
        viewModelScope.launch { chat.open() }
    }

    /** 同一时刻只允许一条在途请求；[stop] 通过取消这个 Job 生效 */
    fun send(text: String) {
        if (sendJob?.isActive == true) return
        sendJob = viewModelScope.launch { chat.send(text) }
    }

    fun stop() {
        sendJob?.cancel()
    }

    fun loadMore() {
        viewModelScope.launch { chat.loadMore() }
    }

    /** 点气泡里的歌曲卡片：把这一组卡片设为队列，从点的那首开始播 */
    fun play(card: Song, cards: List<Song>) {
        if (cards.isEmpty()) return
        session.ensure()
        player.playSongs(cards, startIndex = cards.indexOf(card).coerceAtLeast(0))
    }

    private companion object {
        const val CHAT_TRIM_KEEP = 1_000
    }
}
