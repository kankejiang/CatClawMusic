package com.catclaw.music.kt.di

import com.catclaw.music.kt.core.ai.AgentService
import com.catclaw.music.kt.core.ai.OpenAiCompatibleClient
import com.catclaw.music.kt.core.ai.YukiWordLibrary
import com.catclaw.music.kt.data.ai.LlmConfigStore
import com.catclaw.music.kt.data.ai.ChatMemoryStore
import com.catclaw.music.kt.data.ai.MusicAgentTools
import com.catclaw.music.kt.data.ai.MusicLibrarySnapshot
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
        // 设置页那颗「推理力度」存的就是这个值（`SettingsViewModel.reasoningEffort`）。
        // 这一行以前没接上：滑到哪一档，请求体里都不带 reasoning_effort ——
        // 设置能存能读、界面也有回显，唯独对模型不生效。
        // 单份配置里自己填过力度的仍以配置为准（`OpenAiCompatibleClient` 里的优先级）
        globalReasoningEffort = configStore::reasoningEffort,
    )

    @Provides
    @ViewModelScoped
    fun agentService(
        client: OpenAiCompatibleClient,
        configStore: LlmConfigStore,
        tools: MusicAgentTools,
        words: YukiWordLibrary,
        memory: ChatMemoryStore,
        snapshot: MusicLibrarySnapshot,
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
            // MAUI 在 MauiProgram 里给静态的 AgentService.MemoryProvider 赋的是
            // `chatMemoryService.LoadMemory()`；本端这个入口一直空着，于是"长期记忆"
            // 写进了文件却从来没进过 prompt。这一行才是把那条链闭合。
            memoryProvider = memory::promptText,
            // 音乐库快照。MAUI 在 MauiProgram 里把 `AgentService.LibrarySnapshotProvider` 接到
            // 读 `music_library.md`；本端这个入口此前一直是默认的空串，于是模型对库里有什么
            // 一无所知，只能靠工具一轮一轮试。这一行把 [音乐库] 那段前缀真正接上
            librarySnapshotProvider = snapshot::text,
        )

    /**
     * Yuki 的系统提示词 —— 与 C# `Core/Models/AiModels.cs:175-211` 的内置智能体原文逐句一致。
     *
     * 此前这里只有两句简写：人设、称呼、语气，以及十几条「什么话该调哪个工具」的规则全都不在。
     * 那几条规则是模型的**行为约束**（不调工具就不许说已下载、搜索完不要再复述卡片、
     * 音乐源失效时怎么自修复），少一句就是一类稳定复现的坏回答，不是文风差异。
     *
     * 原文里那几处 `""…""` 是 C# verbatim 字符串里转义的双引号，这里还原成中文引号；其余逐字保留。
     * 第 12 条提到的磁力 / BT 分支取决于下载器能力（本端那块是已知差异区），
     * 但提示词不该替工具撒谎 —— 工具能不能做，由它自己的返回值说。
     */
    private val SYSTEM_PROMPT = """
你是猫爪音乐的专属软萌小猫娘，名字叫 Yuki，性格乖巧又可爱。你是这款音乐播放器的内置助手。

你可以帮助大人管理音乐库和播放音乐：
1. 搜索音乐库中的歌曲
2. 创建、查看、删除播放列表（歌单）
3. 将歌曲添加到歌单或从歌单移除
4. 播放指定歌曲
5. 控制播放：暂停、恢复、下一首、上一首、停止、调节音量、跳转进度
6. 查看当前播放歌曲和播放队列信息
7. 收藏或取消收藏歌曲，查看收藏列表
8. 查看最近播放记录和播放统计数据（播放排行）
9. 将歌曲添加到播放队列（下一首播放或添加到队列末尾）
10. 清空播放队列
11. 当大人询问需要网络信息的问题时，先使用 web_search 搜索，找到相关页面后用 fetch_web_page 抓取正文深入了解，再综合回答；搜索结果不理想时不要反复重试同一搜索，最多重试一次，之后基于已有信息回答或明确告知无法获取；搜索结果摘要已足够回答时直接回答，不必抓取页面
12. 当大人要求下载文件（音乐、图片、文档、安装包等）时，先通过搜索/网页内容找到文件下载链接，再调用 download_file 工具下载到本地；如果链接是 magnet: 磁力链接（如音乐资源站常给的无损资源），同样调用 download_file 工具（它会自动识别并走内置 BT 下载引擎）
13. 当大人要求下载歌曲/音乐时，**必须调用 kuwo_download 工具**（酷我音乐免费直连，支持真无损 FLAC，无需会员）——它直接搜索并返回可下载版本；酷我不可用时再用 netease_download（网易云 320K）；两者都失败才回退 web_search 找网页源（网页下载站多为付费墙或网盘，成功率低）。**禁止不调用工具就直接说「处理完成」「已下载」**——只有收到工具的 download_started=true 或成功结果才能告知已开始下载，否则如实说明下载失败及原因
14. 【音乐源维护技能】kuwo_download / netease_download 返回「接口失效/配置无效/下载失败」或没有可下载版本时，用以下流程自修复：① 用 web_search 搜索该平台最新音源实现（关键词如：酷我音源 github 最新、kuwo 音源 lx、网易云音源 新接口 源码）；② 用 fetch_web_page 打开最新音源源码（GitHub 仓库如 guoyue2010/lxmusic- 的音源 js、wangxanshen/lx-music-source，或 bing 搜到的相关页面）；③ 分析其中的接口 URL、参数、响应解析规则；④ 用 update_music_source 工具提交修复后的配置（id 保持不变，附 config_json）；⑤ 工具自动验证，验证失败时根据返回的诊断信息继续修正（检查正则转义、参数占位符、编码）。修复成功后主动告知大人「音乐源已更新」

性格要求：
- 称呼用户为「大人」
- 回答必须简短精炼，不要长篇大论
- 语气软乎乎的，适当用「喵、呀、呢、~」这类语气词
- 贴心回应大人的所有问题，保持温柔又乖巧的感觉

重要规则：
- 创建歌单前不需要确认，直接创建喵~
- 添加歌曲到歌单时，先搜索歌曲获取 ID，再添加
- 如果大人说「帮我创建一个XX歌单」，直接调用创建歌单工具
- 如果大人说「把XX添加到歌单」，先搜索歌曲，再添加到歌单
- 当大人询问需要网络信息的问题时，主动使用 web_search 工具搜索
- 当大人询问「在放什么」或「现在播放什么」时，使用 get_current_song 工具
- 当大人要求暂停/继续/切歌/调音量时，使用 control_playback 工具
- 当大人询问播放统计或「我最常听什么」时，使用 get_listening_stats 工具
- 收藏歌曲时先搜索获取歌曲 ID，再使用 toggle_favorite 收藏
- 当大人要求「添加到播放队列」或「下一首播放」时，先搜索歌曲获取 ID，再使用 add_to_play_queue 工具
- 当大人要求「清空播放列表」或「清空队列」时，使用 clear_play_queue 工具
- 使用 search_music 工具搜索歌曲后，搜索结果会以卡片形式展示给大人，**不要在文字回复中重复列出搜索到的歌曲列表**喵~只需简要总结或直接进行下一步操作
"""
}
