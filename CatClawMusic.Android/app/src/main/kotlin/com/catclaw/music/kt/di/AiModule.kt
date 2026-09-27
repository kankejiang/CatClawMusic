package com.catclaw.music.kt.di

import com.catclaw.music.kt.core.ai.AgentService
import com.catclaw.music.kt.core.ai.OpenAiCompatibleClient
import com.catclaw.music.kt.core.ai.YukiWordLibrary
import com.catclaw.music.kt.data.ai.LlmConfigStore
import com.catclaw.music.kt.data.ai.MusicAgentTools
import com.catclaw.music.kt.data.ai.OkHttpLlmTransport
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.components.ViewModelComponent
import dagger.hilt.android.scopes.ViewModelScoped
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Module
@InstallIn(ViewModelComponent::class)
object AiModule {

    @Provides
    @ViewModelScoped
    fun llmClient(configStore: LlmConfigStore): OpenAiCompatibleClient = OpenAiCompatibleClient(
        transport = OkHttpLlmTransport(),
        configProvider = configStore::current,
        fallbackProvider = configStore::fallbacks,
    )

    @Provides
    @ViewModelScoped
    fun agentService(
        client: OpenAiCompatibleClient,
        configStore: LlmConfigStore,
        tools: MusicAgentTools,
        words: YukiWordLibrary,
    ): AgentService = AgentService(
            // 阻塞的 HTTP 调用必须离开主线程：C# 那边同一位置就是用 Task.Run 包住的，
            // 否则 Android 上从 UI 线程发请求会抛 NetworkOnMainThreadException
            chat = { messages, defs, onDelta ->
                withContext(Dispatchers.IO) { client.chat(messages, defs, onDelta) }
            },
            tools = tools.all,
            systemPrompt = { SYSTEM_PROMPT },
            maxToolRounds = configStore::maxToolRounds,
            searchSongs = tools::searchCards,
            volumePercent = tools::volumePercent,
            // 已配置模型时，词库作为语气样本注入系统提示（与 MAUI 同一用法）
            personalityKnowledge = { words.knowledgePrompt() },
        )

    private const val SYSTEM_PROMPT =
        "你是猫爪音乐的助手 Yuki。可以用工具查询与操作本机音乐库；" +
            "回答保持简短、口语化，不要用 Markdown 列表复述你已经用工具拿到的结果。"
}
