using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// AI 智能体服务实现，负责管理对话上下文、调度 LLM 客户端与工具，完成多轮工具调用循环。
/// </summary>
public class AgentService : IAgentService
{
    /// <summary>底层 LLM 客户端，用于实际发起对话请求</summary>
    private readonly ILlmClient _llmClient;

    /// <summary>可被 LLM 调用的本地工具集合</summary>
    private readonly IEnumerable<IAgentTool> _tools;

    /// <summary>当前对话历史消息列表（包含 system / user / assistant / tool 角色）</summary>
    private readonly List<ChatMessage> _conversationHistory = new();

    /// <summary>日志服务，用于记录 LLM 请求/响应与工具调用情况</summary>
    private readonly ILogService _logService;

    /// <summary>音乐库服务（可选），用于在搜索音乐时附加歌曲上下文</summary>
    private readonly IMusicLibraryService? _musicLibrary;

    /// <summary>音频播放器（可选），用于本地命令读取当前音量等播放状态</summary>
    private readonly IAudioPlayerService? _player;

    /// <summary>当前智能体 ID，决定使用的系统提示词</summary>
    private string _currentAgentId;

    /// <summary>根据当前智能体 ID 获取对应的系统提示词</summary>
    private string CurrentSystemPrompt => BuiltinAgent.GetById(_currentAgentId).SystemPrompt;

    /// <summary>静态配置存储实例，在 DI 初始化时设置</summary>
    private static IAgentConfigStorage? _staticConfigStorage;

    /// <summary>音乐库快照内容提供者（由 MAUI 层在启动时赋值）</summary>
    public static Func<string>? LibrarySnapshotProvider { get; set; }

    /// <summary>长期记忆内容提供者（由 MAUI 层在启动时赋值）</summary>
    public static Func<string>? MemoryProvider { get; set; }

    /// <summary>Yuki 人格词库知识提供者（由 MAUI 层在启动时赋值），注入系统提示词让模型模仿语气</summary>
    public static Func<Task<string>>? PersonalityKnowledgeProvider { get; set; }

    /// <summary>初始化静态配置存储（由 DI 容器在启动时调用）</summary>
    /// <param name="configStorage">配置存储实现</param>
    public static void Initialize(IAgentConfigStorage configStorage)
    {
        _staticConfigStorage = configStorage;
    }

    /// <summary>获取已注入的配置存储实例，未初始化时抛出异常</summary>
    private static IAgentConfigStorage ConfigStorage =>
        _staticConfigStorage ?? throw new InvalidOperationException("AgentService 未初始化，请先调用 AgentService.Initialize()");

    /// <summary>当前智能体是否已完成配置（启用且填入了 ApiUrl 与 ApiKey）</summary>
    public bool IsConfigured
    {
        get
        {
            var config = LoadConfig();
            return config.Enabled && !string.IsNullOrWhiteSpace(config.ApiUrl) && !string.IsNullOrWhiteSpace(config.ApiKey);
        }
    }

    /// <summary>
    /// 尝试本地解析并执行基础播放命令，无需 LLM 模型。
    /// 复用已注册的 control_playback / get_current_song 工具执行，命中返回回复，未命中返回 null。
    /// </summary>
    public async Task<ChatMessage?> TryLocalCommandAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return null;
        var text = userMessage.Trim();

        var toolMap = _toolMap;

        // ── 播放控制：暂停/下一首/上一首/停止/继续 ──
        string? action = null;
        if (ContainsAny(text, "暂停")) action = "pause";
        else if (ContainsAny(text, "下一首", "下一曲", "切歌", "切到下一")) action = "next";
        else if (ContainsAny(text, "上一首", "上一曲", "切到上一")) action = "previous";
        else if (ContainsAny(text, "停止")) action = "stop";
        else if (ContainsAny(text, "继续", "恢复")
                 || text.Equals("播放", StringComparison.OrdinalIgnoreCase)
                 || text.Equals("resume", StringComparison.OrdinalIgnoreCase)
                 || text.Equals("play", StringComparison.OrdinalIgnoreCase))
            action = "resume";

        if (action != null && toolMap.TryGetValue("control_playback", out var controlTool))
        {
            var result = await controlTool.ExecuteAsync(JsonSerializer.Serialize(new { action }));
            return new ChatMessage { Role = "assistant", Content = ExtractToolMessage(result) };
        }

        // ── 当前歌曲 ──
        if (ContainsAny(text, "当前歌曲", "当前播放", "在放什么", "正在播放什么", "放的什么", "在听什么", "什么歌")
            && toolMap.TryGetValue("get_current_song", out var currentTool))
        {
            var result = await currentTool.ExecuteAsync("{}");
            return new ChatMessage { Role = "assistant", Content = FormatCurrentSong(result) };
        }

        // ── 音量：调大/调小/静音 ──（需当前音量，依赖 _player）
        if (_player != null && toolMap.TryGetValue("control_playback", out var volTool))
        {
            var currentVol = (int)Math.Round(_player.Volume * 100);
            int? targetVol = null;
            string? volReply = null;
            if (ContainsAny(text, "静音"))
            {
                targetVol = 0; volReply = "已静音";
            }
            else if (ContainsAny(text, "大点声", "大声", "音量调大", "音量加大", "调大音量", "声音大"))
            {
                targetVol = Math.Clamp(currentVol + 10, 0, 100); volReply = $"音量已调大到 {targetVol}";
            }
            else if (ContainsAny(text, "小点声", "小声", "音量调小", "音量减小", "调小音量", "声音小"))
            {
                targetVol = Math.Clamp(currentVol - 10, 0, 100); volReply = $"音量已调小到 {targetVol}";
            }

            if (targetVol != null)
            {
                await volTool.ExecuteAsync(JsonSerializer.Serialize(new { volume = targetVol.Value }));
                return new ChatMessage { Role = "assistant", Content = volReply! };
            }
        }

        // ── 搜索：歌名/歌手/专辑（无模型时也能搜，结果以可点击播放的卡片返回）──
        if (_musicLibrary != null)
        {
            var keyword = ExtractSearchKeyword(text);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                try
                {
                    var songs = await _musicLibrary.SearchAsync(keyword);
                    if (songs != null && songs.Count > 0)
                    {
                        // 取前 10 首作为卡片（非滚动渲染，避免气泡过高），多余数量在文案中提示
                        var top = songs.Take(10).ToList();
                        var more = songs.Count > top.Count ? $"，还有 {songs.Count - top.Count} 首未显示" : "";
                        return new ChatMessage
                        {
                            Role = "assistant",
                            Content = $"找到 {songs.Count} 首和「{keyword}」相关的歌曲喵{more}，点一下就能播放~",
                            Songs = top
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logService.Warn("Agent", $"本地搜索失败: {ex.Message}");
                }
            }
        }

        return null; // 未命中任何基础命令
    }

    /// <summary>搜索动词（按长度降序，优先匹配长动词），命中即视为搜索意图。</summary>
    private static readonly string[] SearchVerbs =
        { "搜一下", "查找", "找一下", "我想听", "来一首", "帮我搜", "帮我找", "搜索", "播放", "搜", "找", "放", "听" };

    /// <summary>搜索关键词中需剔除的限定词。</summary>
    private static readonly string[] SearchQualifiers = { "歌曲", "音乐", "专辑", "歌手", "一下", "歌" };

    /// <summary>从用户输入中提取搜索关键词：取最靠前的搜索动词之后的内容，并去掉常见限定词。无搜索意图返回 null。</summary>
    private static string? ExtractSearchKeyword(string text)
    {
        int bestIdx = int.MaxValue;
        int bestEnd = -1;
        foreach (var verb in SearchVerbs)
        {
            var idx = text.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < bestIdx)
            {
                bestIdx = idx;
                bestEnd = idx + verb.Length;
            }
        }
        if (bestEnd < 0) return null;

        var keyword = text.Substring(bestEnd).Trim();
        foreach (var q in SearchQualifiers)
            keyword = keyword.Replace(q, "");
        keyword = keyword.Trim();
        return keyword.Length > 0 ? keyword : null;
    }

    /// <summary>判断文本是否包含任意关键词（忽略大小写）。</summary>
    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>从工具返回的 JSON 中提取 message 或 error 文本作为友好回复。</summary>
    private static string ExtractToolMessage(string toolResultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? "操作完成";
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return err.GetString() ?? "操作失败";
        }
        catch { }
        return "操作完成";
    }

    /// <summary>将 get_current_song 工具的 JSON 结果格式化为友好的当前播放信息。</summary>
    private static string FormatCurrentSong(string toolResultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return err.GetString() ?? "当前没有正在播放的歌曲";
            if (root.TryGetProperty("song", out var song))
            {
                var title = song.TryGetProperty("Title", out var t) ? t.GetString() : null;
                var artist = song.TryGetProperty("Artist", out var a) ? a.GetString() : null;
                var isPlaying = root.TryGetProperty("is_playing", out var ip) && ip.GetBoolean();
                if (string.IsNullOrEmpty(title)) return "当前没有正在播放的歌曲";
                var state = isPlaying ? "正在播放" : "当前暂停";
                return string.IsNullOrEmpty(artist) ? $"{state}：「{title}」" : $"{state}：「{title}」- {artist}";
            }
        }
        catch { }
        return "当前没有正在播放的歌曲";
    }

    /// <summary>
    /// 构造 AgentService 实例
    /// </summary>
    /// <param name="llmClient">LLM 客户端</param>
    /// <param name="tools">可用的工具集合</param>
    /// <param name="logService">日志服务</param>
    /// <param name="musicLibrary">音乐库服务（可选）</param>
    public AgentService(ILlmClient llmClient, IEnumerable<IAgentTool> tools, ILogService logService, IMusicLibraryService? musicLibrary = null, IAudioPlayerService? player = null)
    {
        _llmClient = llmClient;
        _tools = tools;
        _logService = logService;
        _musicLibrary = musicLibrary;
        _player = player;
        _currentAgentId = LoadCurrentAgentId();
        // 工具集合在构造后固定：定义与查找表缓存一次，避免每条消息/每个工具轮次重复构建
        _toolDefs = _tools.Select(t => t.GetDefinition()).ToList();
        _toolMap = _tools.ToDictionary(t => t.Name);
    }

    /// <summary>工具函数定义（构造时缓存）</summary>
    private readonly List<ToolDefinition> _toolDefs;
    /// <summary>工具名 → 工具实例查找表（构造时缓存）</summary>
    private readonly Dictionary<string, IAgentTool> _toolMap;

    /// <summary>获取所有支持的 LLM 服务商列表</summary>
    public static LlmProviderInfo[] GetProviders() => LlmProviderInfo.GetAll();

    /// <summary>从配置存储加载全部 LLM 配置列表</summary>
    public static List<LlmConfig> LoadAllConfigs()
    {
        try
        {
            var json = ConfigStorage.GetString("all_configs", "[]") ?? "[]";
            var configs = JsonSerializer.Deserialize<List<LlmConfig>>(json) ?? new List<LlmConfig>();
            return configs;
        }
        catch
        {
            return new List<LlmConfig>();
        }
    }

    /// <summary>保存全部 LLM 配置列表到配置存储</summary>
    /// <param name="configs">待保存的配置列表</param>
    public static void SaveAllConfigs(List<LlmConfig> configs)
    {
        var json = JsonSerializer.Serialize(configs);
        ConfigStorage.SetString("all_configs", json);
    }

    /// <summary>加载当前生效的 LLM 配置（按 current_config_name 索引，未找到则按旧字段读取）</summary>
    public static LlmConfig LoadConfig()
    {
        try
        {
            var currentName = ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
            var allConfigs = LoadAllConfigs();
            var config = allConfigs.FirstOrDefault(c => c.Name == currentName);

            if (config != null)
                return config;

            return new LlmConfig
            {
                Name = currentName,
                Provider = ConfigStorage.GetString("provider", "deepseek") ?? "deepseek",
                ApiUrl = ConfigStorage.GetString("api_url", "https://api.deepseek.com/v1") ?? "",
                ApiKey = ConfigStorage.GetString("api_key", "") ?? "",
                Model = ConfigStorage.GetString("model", "deepseek-v4-flash") ?? "deepseek-v4-flash",
                Temperature = (double)ConfigStorage.GetFloat("temperature", 0.7f),
                MaxTokens = ConfigStorage.GetInt("max_tokens", 2048),
                Enabled = ConfigStorage.GetBool("enabled", false)
            };
        }
        catch
        {
            return new LlmConfig();
        }
    }

    /// <summary>保存单个 LLM 配置，并同步更新当前生效配置与兼容字段。</summary>
    /// <param name="config">待保存的配置</param>
    public static void SaveConfig(LlmConfig config)
    {
        var allConfigs = LoadAllConfigs();
        var existingIndex = allConfigs.FindIndex(c => c.Name == config.Name);

        if (existingIndex >= 0)
        {
            allConfigs[existingIndex] = config;
        }
        else
        {
            allConfigs.Add(config);
        }

        SaveAllConfigs(allConfigs);

        // ⚠ 仅当保存的配置**就是当前生效配置**时才同步兼容字段，
        // 且绝不无条件把 current_config_name 改为本配置——否则在模型管理页
        // "切换备"（保存备用模型）时会把主模型悄悄切到该模型。
        var currentName = ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
        if (config.Name == currentName)
        {
            ConfigStorage.SetString("provider", config.Provider);
            ConfigStorage.SetString("api_url", config.ApiUrl);
            ConfigStorage.SetString("api_key", config.ApiKey);
            ConfigStorage.SetString("model", config.Model);
            ConfigStorage.SetFloat("temperature", (float)config.Temperature);
            ConfigStorage.SetInt("max_tokens", config.MaxTokens);
            ConfigStorage.SetBool("enabled", config.Enabled);
        }
    }

    /// <summary>删除指定名称的 LLM 配置</summary>
    /// <param name="configName">配置名称</param>
    public static void DeleteConfig(string configName)
    {
        var allConfigs = LoadAllConfigs();
        var toRemove = allConfigs.FirstOrDefault(c => c.Name == configName);
        if (toRemove != null)
        {
            allConfigs.Remove(toRemove);
            SaveAllConfigs(allConfigs);
        }
    }

    /// <summary>获取当前生效的 LLM 配置名称</summary>
    public static string GetCurrentConfigName()
    {
        return ConfigStorage.GetString("current_config_name", "默认配置") ?? "默认配置";
    }

    /// <summary>设置当前生效的 LLM 配置名称</summary>
    /// <param name="name">配置名称</param>
    public static void SetCurrentConfigName(string name)
    {
        ConfigStorage.SetString("current_config_name", name);
    }

    /// <summary>
    /// 发送用户消息并获取 AI 回复，支持多轮工具调用与流式部分消息回调。
    /// <para>最多进行 5 轮工具调用循环，超出后将返回提示消息。</para>
    /// </summary>
    /// <param name="userMessage">用户输入文本</param>
    /// <param name="onPartialMessage">流式输出或工具调用阶段的回调</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终助手回复消息</returns>
    public async Task<ChatMessage> SendMessageAsync(string userMessage, Action<ChatMessage>? onPartialMessage = null, CancellationToken ct = default)
    {
        if (_conversationHistory.Count == 0)
        {
            // 固定前缀组装顺序：系统提示 → 记忆 → 项目上下文 → 对话历史 → 新消息
            // 拆分为独立的 system 消息以提高 API 端的缓存命中率，降低 token 消耗

            // 1. 系统提示（最稳定，缓存命中率高）
            _conversationHistory.Add(new ChatMessage { Role = "system", Content = CurrentSystemPrompt });

            // 1.5 Yuki 人格词库知识（让模型模仿可爱/傲娇语气）
            try
            {
                var personality = PersonalityKnowledgeProvider != null ? await PersonalityKnowledgeProvider.Invoke() : null;
                if (!string.IsNullOrEmpty(personality))
                {
                    if (personality.Length > 2000)
                        personality = personality[..2000] + "\n..";
                    _conversationHistory.Add(new ChatMessage { Role = "system", Content = personality });
                }
            }
            catch { }

            try
            {
                var libraryContent = LibrarySnapshotProvider?.Invoke() ?? string.Empty;
                var memoryContent = MemoryProvider?.Invoke() ?? string.Empty;

                // 2. 记忆（偶尔变化，放在系统提示之后）
                if (!string.IsNullOrEmpty(memoryContent))
                {
                    var memoryLines = memoryContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var recentMemory = string.Join('\n', memoryLines.TakeLast(15));
                    if (recentMemory.Length > 300)
                        recentMemory = recentMemory[^300..];
                    _conversationHistory.Add(new ChatMessage { Role = "system", Content = $"[记忆]\n{recentMemory}" });
                }

                // 3. 项目上下文（音乐库，变化频率略高于记忆）
                if (!string.IsNullOrEmpty(libraryContent))
                {
                    if (libraryContent.Length > 600)
                        libraryContent = libraryContent[..600] + "..";
                    _conversationHistory.Add(new ChatMessage { Role = "system", Content = $"[音乐库]\n{libraryContent}" });
                }
            }
            catch { }
        }

        _conversationHistory.Add(new ChatMessage { Role = "user", Content = userMessage });

        var toolDefs = _toolDefs;
        var toolMap = _toolMap;

        TrimConversationHistory();

        // 执行轮数上限只认全局设置，0表示不限
        int maxToolRounds = ConfigStorage.GetInt(AgentRunSettings.KeyMaxToolRounds, AgentRunSettings.DefaultMaxToolRounds);
        if (maxToolRounds <= 0) maxToolRounds = int.MaxValue;

        // 累积推理模型每一轮的思考内容，最终附到回复上供 UI 思考区展示
        var reasoningBuilder = new System.Text.StringBuilder();

        for (int round = 0; round < maxToolRounds; round++)
        {
            LlmResponse response;
            try
            {
                var requestMessages = BuildRequestMessages();
                // ChatAsync 本身是 async HTTP 调用，无需 Task.Run 包装（多余线程跳转 + Task 分配）
                response = await _llmClient.ChatAsync(requestMessages, toolDefs, ct).ConfigureAwait(false);
                _logService.Info("Agent", $"Agent LLM 响应: content='{Truncate(response.Content, 200)}', toolCalls={response.ToolCalls.Count}, finishReason={response.FinishReason}");
                if (!string.IsNullOrEmpty(response.ReasoningContent))
                {
                    if (reasoningBuilder.Length > 0) reasoningBuilder.Append('\n');
                    reasoningBuilder.Append(response.ReasoningContent);
                }
            }
            catch (Exception ex)
            {
                _logService.Warn("Agent", $"Agent LLM 请求失败: {ex.Message}");
                var errorMsg = new ChatMessage { Role = "assistant", Content = $"抱歉，AI 服务请求失败：{ex.Message}" };
                _conversationHistory.Add(errorMsg);
                return errorMsg;
            }

            if (!response.HasToolCalls)
            {
                var assistantMsg = new ChatMessage { Role = "assistant", Content = response.Content };
                if (reasoningBuilder.Length > 0)
                    assistantMsg.ReasoningContent = reasoningBuilder.ToString();
                _conversationHistory.Add(assistantMsg);
                return assistantMsg;
            }

            var assistantToolCallMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content ?? "",
                ToolCalls = response.ToolCalls
            };
            _conversationHistory.Add(assistantToolCallMsg);
            onPartialMessage?.Invoke(assistantToolCallMsg);

            foreach (var toolCall in response.ToolCalls)
            {
                string toolResult;
                List<Song>? songs = null;
                if (toolMap.TryGetValue(toolCall.Function.Name, out var tool))
                {
                    try
                    {
                        toolResult = await tool.ExecuteAsync(toolCall.Function.Arguments);
                        _logService.Info("Agent", $"Agent 工具 {toolCall.Function.Name} 执行成功");

                        if (toolCall.Function.Name == "search_music" && _musicLibrary != null)
                        {
                            try
                            {
                                var keyword = ArgHelper.ExtractStringArgFallback(toolCall.Function.Arguments, "keyword");
                                if (!string.IsNullOrWhiteSpace(keyword))
                                    songs = await _musicLibrary.SearchAsync(keyword);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        toolResult = JsonSerializer.Serialize(new { error = $"工具执行失败: {ex.Message}" });
                        _logService.Warn("Agent", $"Agent 工具 {toolCall.Function.Name} 执行失败: {ex.Message}");
                    }
                }
                else
                {
                    toolResult = JsonSerializer.Serialize(new { error = $"未知工具: {toolCall.Function.Name}" });
                }

                if (toolResult.Length > 1000)
                    toolResult = toolResult[..1000] + "..(截断)";

                var toolResultMsg = new ChatMessage
                {
                    Role = "tool",
                    Content = toolResult,
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Function.Name,
                    Songs = songs
                };
                _conversationHistory.Add(toolResultMsg);
                onPartialMessage?.Invoke(toolResultMsg);
            }
        }

        var finalMsg = new ChatMessage { Role = "assistant", Content = "操作步骤过多，已停止执行。请尝试简化你的请求。" };
        _conversationHistory.Add(finalMsg);
        return finalMsg;
    }

    private List<ChatMessage> BuildRequestMessages()
    {
        var messages = new List<ChatMessage>(_conversationHistory);

        int CalculateEstimatedTokens()
        {
            int total = 0;
            foreach (var m in messages)
            {
                total += 6;
                if (!string.IsNullOrEmpty(m.Content))
                    total += m.Content.Length / 2;
                if (m.ToolCalls != null)
                {
                    foreach (var tc in m.ToolCalls)
                    {
                        total += 80;
                        if (!string.IsNullOrEmpty(tc.Function.Arguments))
                            total += tc.Function.Arguments.Length / 2;
                    }
                }
            }
            total += 1200;
            return total;
        }

        // 上下文预算：Agent 工具循环会累积大量 tool 消息，4500 只够几轮就会把
        // 早期工具结果/历史裁光 → 决策链断裂。新模型（DeepSeek V4 / GLM-5.2 /
        // Kimi K3 等）上下文 128K~1M，预算提到 32000（128K 的 1/4，安全余量），
        // 保留 3 条消息底线防止 system 也被裁掉。
        while (messages.Count > 3 && CalculateEstimatedTokens() > 32000)
        {
            int removeIdx = -1;
            for (int i = 1; i < messages.Count; i++)
            {
                if (messages[i].Role != "system")
                {
                    removeIdx = i;
                    break;
                }
            }
            if (removeIdx < 0) break;
            messages.RemoveAt(removeIdx);
        }

        return messages;
    }

    /// <summary>清空当前对话历史</summary>
    public void ClearConversation()
    {
        _conversationHistory.Clear();
    }

    /// <summary>
    /// 裁剪对话历史：保留 system 消息 + 最近 N 轮对话，防止 token 超限。
    /// Agent 工具循环一轮包含 user+assistant+多条 tool 消息，保留 24 条
    /// （约 12 轮工具循环），远高于旧值 10 条，避免多轮工具调用中途丢上下文。
    /// </summary>
    private void TrimConversationHistory()
    {
        if (_conversationHistory.Count <= 24) return;

        var systemMsgs = _conversationHistory.Where(m => m.Role == "system").ToList();
        var recent = _conversationHistory
            .Where(m => m.Role != "system")
            .TakeLast(20)
            .ToList();

        _conversationHistory.Clear();
        _conversationHistory.AddRange(systemMsgs);
        _conversationHistory.AddRange(recent);
    }

    /// <summary>
    /// 一次性快速问答：使用独立临时对话，不污染主对话历史，也不注入音乐库/记忆上下文。
    /// 用于 AI 推荐理由等后台自动生成场景。
    /// </summary>
    public async Task<string> QuickAskAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var tempMessages = new List<ChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = userPrompt }
        };

        try
        {
            var response = await Task.Run(() => _llmClient.ChatAsync(tempMessages, null, ct), ct);
            return (response.Content ?? string.Empty).Trim();
        }
        catch (Exception ex)
        {
            _logService.Warn("Agent", $"QuickAsk 失败: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>获取当前对话历史消息列表的副本</summary>
    public List<ChatMessage> GetConversationHistory() => _conversationHistory.ToList();

    /// <summary>获取当前正在使用的内置智能体</summary>
    public BuiltinAgent GetCurrentAgent() => BuiltinAgent.GetById(_currentAgentId);

    /// <summary>切换当前智能体，并清空对话历史</summary>
    /// <param name="agentId">智能体 ID</param>
    public void SetCurrentAgent(string agentId)
    {
        _currentAgentId = agentId;
        SaveCurrentAgentId(agentId);
        _conversationHistory.Clear();
    }

    /// <summary>从配置存储加载当前智能体 ID（默认 yuki）</summary>
    public static string LoadCurrentAgentId()
    {
        try
        {
            return ConfigStorage.GetString("current_agent_id", "yuki") ?? "yuki";
        }
        catch
        {
            return "yuki";
        }
    }

    /// <summary>持久化当前智能体 ID 到配置存储</summary>
    /// <param name="agentId">智能体 ID</param>
    public static void SaveCurrentAgentId(string agentId)
    {
        ConfigStorage.SetString("current_agent_id", agentId);
    }

    // ─── Agent 全局运行设置 ───

    /// <summary>获取执行轮数上限（0=不限）</summary>
    public static int GetMaxToolRounds()
        => ConfigStorage.GetInt(AgentRunSettings.KeyMaxToolRounds, AgentRunSettings.DefaultMaxToolRounds);

    /// <summary>设置执行轮数上限（0=不限）</summary>
    public static void SetMaxToolRounds(int value)
        => ConfigStorage.SetInt(AgentRunSettings.KeyMaxToolRounds, value);

    /// <summary>获取规划轮数上限（0=不限）</summary>
    public static int GetMaxPlanRounds()
        => ConfigStorage.GetInt(AgentRunSettings.KeyMaxPlanRounds, AgentRunSettings.DefaultMaxPlanRounds);

    /// <summary>设置规划轮数上限（0=不限）</summary>
    public static void SetMaxPlanRounds(int value)
        => ConfigStorage.SetInt(AgentRunSettings.KeyMaxPlanRounds, value);

    /// <summary>获取全局推理力度</summary>
    public static string GetReasoningEffort()
        => ConfigStorage.GetString(AgentRunSettings.KeyReasoningEffort, AgentRunSettings.DefaultReasoningEffort) ?? AgentRunSettings.DefaultReasoningEffort;

    /// <summary>设置全局推理力度</summary>
    public static void SetReasoningEffort(string value)
        => ConfigStorage.SetString(AgentRunSettings.KeyReasoningEffort, value);

    /// <summary>截断字符串到指定长度，超出部分以 "..." 结尾（用于日志输出）</summary>
    /// <param name="s">原字符串</param>
    /// <param name="maxLen">最大长度</param>
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
