using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>聊天域：聊天历史加载 / AI 对话发送 / 消息裁剪与记忆提取。</summary>
public partial class SearchViewModel
{
    /// <summary>当前正在执行的 AI 请求取消令牌源；为 null 表示空闲（仅聊天域使用）</summary>
    private CancellationTokenSource? _agentCts;

    /// <summary>发送按钮是否可见（AI 空闲时显示发送）</summary>
    public bool IsSendVisible => !IsAgentThinking;

    /// <summary>中断按钮是否可见（AI 思考/回复中显示停止）</summary>
    public bool IsStopVisible => IsAgentThinking;

    /// <summary>IsAgentThinking 变化时同步两个按钮的显隐</summary>
    partial void OnIsAgentThinkingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSendVisible));
        OnPropertyChanged(nameof(IsStopVisible));
    }

    /// <summary>中断当前正在进行的 AI 回复（思考或生成中调用），无请求时忽略</summary>
    public void StopAgent()
    {
        _agentCts?.Cancel();
    }

    private async Task LoadRecentChatHistoryAsync()
    {
        try
        {
            const int pageSize = 30;
            var records = await _database.GetRecentChatMessagesAsync(pageSize);
            if (records.Count == 0)
            {
                // 整体替换集合，一次性通知 UI（避免多次集合变更触发旋转列表反复布局）
                ChatMessages = new ObservableCollection<ObservableChatMessage>
                {
                    new ObservableChatMessage
                    {
                        Role = "assistant",
                        Content = _agentService.IsConfigured
                            ? "Yuki 在这里喵，可以帮你找歌、放歌、建歌单。"
                            : "Yuki 在这里喵，不过 AI 还没配置，先去设置页完成配置吧。"
                    }
                };
                _oldestLoadedMessageId = 0;
                HasMoreChatHistory = false;
            }
            else
            {
                // 数据库返回正序（旧→新），倒序构建使最新消息在 index 0；
                // 构建好后整体替换集合，一次性通知 UI（替代 Clear+逐条 Add 的 N 次集合变更）。
                var list = new List<ObservableChatMessage>(records.Count);
                for (int i = records.Count - 1; i >= 0; i--)
                    list.Add(new ObservableChatMessage { Role = records[i].Role, Content = records[i].Content });
                ChatMessages = new ObservableCollection<ObservableChatMessage>(list);
                _oldestLoadedMessageId = records[0].Id;
                // 取满一页说明可能还有更多，免去额外的全表 COUNT 查询
                HasMoreChatHistory = records.Count == pageSize;
            }

            ChatHistoryLoaded?.Invoke(this, new ChatHistoryLoadedEventArgs { IsInitialLoad = true, ScrollToEnd = true });
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] LoadChatHistory failed: {ex.Message}");
        }
    }

    /// <summary>向上翻页时加载更多历史记录，追加到列表末尾（倒序模式下末尾 = 最旧）</summary>
    public async Task LoadMoreChatHistoryAsync()
    {
        if (_isLoadingMoreHistory || !HasMoreChatHistory || _oldestLoadedMessageId <= 0)
            return;

        _isLoadingMoreHistory = true;
        try
        {
            var older = await _database.GetRecentChatMessagesAsync(20, _oldestLoadedMessageId);
            if (older.Count > 0)
            {
                // 数据库返回正序（旧→新），倒序追加使更旧的消息在列表末尾
                for (int i = older.Count - 1; i >= 0; i--)
                {
                    ChatMessages.Add(new ObservableChatMessage { Role = older[i].Role, Content = older[i].Content });
                }
                _oldestLoadedMessageId = older[0].Id;
                // 取满一页说明可能还有更多，免去额外的全表 COUNT 查询
                HasMoreChatHistory = older.Count == 20;

                // 倒序模式下末尾追加不改变已有项 index，无需滚动位置修复
                ChatHistoryLoaded?.Invoke(this, new ChatHistoryLoadedEventArgs
                {
                    IsInitialLoad = false
                });
            }
            else
            {
                HasMoreChatHistory = false;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] LoadMoreChatHistory failed: {ex.Message}");
        }
        finally
        {
            _isLoadingMoreHistory = false;
        }
    }

    private void EnterChatMode()
    {
        IsChatMode = true;
        // 异步加载历史记录
        _ = LoadRecentChatHistoryAsync();
        EnterChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitChatMode()
    {
        IsChatMode = false;
        ExitChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 异步加载探索数据：每日推荐、艺术家、专辑、最多播放、最新音乐。
    /// 同日重复加载时跳过每日推荐生成，仅刷新随播放变化的列表。
    /// </summary>
    public async Task SendMessageFromSearchAsync(string message)
    {
        EnterChatMode();
        ChatInput = message;
        await SendMessageAsync();
    }

    /// <summary>发送聊天消息：将用户输入发送给 Agent 并追加回复。思考过程内嵌于助手气泡，发送新消息时自动折叠上条。</summary>
    public async Task SendMessageAsync()
    {
        var userMessage = ChatInput?.Trim();
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        ChatInput = "";

        // 发送新消息时自动折叠上一条助手消息的思考过程
        if (_currentThinkingMessage != null)
        {
            _currentThinkingMessage.IsThinkingExpanded = false;
        }

        var userMsg = new ObservableChatMessage
        {
            Role = "user",
            Content = userMessage
        };
        // 倒序模式：新消息插入到头部（index 0），翻转后显示在视觉底部
        ChatMessages.Insert(0, userMsg);
        _ = SaveChatMessageSafeAsync(new ChatMessageRecord { Role = "user", Content = userMessage, Timestamp = DateTime.UtcNow });
        _chatMemoryService.RecordMessage(userMsg);
        _ = TrimOldChatMessagesAsync();

        if (!_agentService.IsConfigured)
        {
            // 未配置模型时，先尝试本地命令完成播放器基本操作（暂停/播放/切歌/音量等）
            ChatMessage? localReply = null;
            try { localReply = await _agentService.TryLocalCommandAsync(userMessage); }
            catch (Exception ex) { Log.Debug("SearchViewModel", $"[SearchVM] 本地命令执行失败: {ex.Message}"); }

            string? replyContent = localReply?.Content;
            var replySongs = localReply?.Songs;

            // 非播放/搜索命令时，用 Yuki 人格词库回复（SQLite 词库按需查询）
            if (string.IsNullOrEmpty(replyContent))
            {
                try { replyContent = await Services.YukiWordLibrary.Instance.GetReplyAsync(userMessage); }
                catch (Exception ex) { Log.Debug("SearchViewModel", $"[SearchVM] 词库回复失败: {ex.Message}"); }
            }

            // 词库也无内容时才回退到"未配置"提示
            if (string.IsNullOrEmpty(replyContent))
                replyContent = "AI 还没有配置好喵，先到“设置 > AI 设置”里填一下模型信息吧。\n\n不过基础的播放控制我可以直接帮你：暂停 / 播放 / 下一首 / 上一首 / 停止 / 当前歌曲 / 调音量。";

            var notConfiguredMsg = new ObservableChatMessage
            {
                Role = "assistant",
                Content = replyContent,
                Songs = replySongs
            };
            // 倒序模式：新消息插入到头部
            ChatMessages.Insert(0, notConfiguredMsg);
            _ = SaveChatMessageSafeAsync(new ChatMessageRecord { Role = "assistant", Content = notConfiguredMsg.Content, Timestamp = DateTime.UtcNow });
            _chatMemoryService.RecordMessage(notConfiguredMsg);
            _currentThinkingMessage = null;
            _ = TriggerMemoryExtractionAsync();
            return;
        }

        // 立即创建助手占位气泡，思考过程内嵌其中（默认展开）
        var assistantMsg = new ObservableChatMessage
        {
            Role = "assistant",
            Content = "",
            IsThinking = true,
            IsThinkingExpanded = true
        };
        assistantMsg.ThinkingSteps.Add("💭 正在思考你的问题...");
        // 倒序模式：助手占位消息插入到头部，出现在用户消息之上（视觉下方）
        ChatMessages.Insert(0, assistantMsg);
        _currentThinkingMessage = assistantMsg;
        IsAgentThinking = true;
        ScrollToLatestMessageRequested?.Invoke(this, EventArgs.Empty);

        try
        {
            _agentCts?.Cancel();
            _agentCts?.Dispose();
            _agentCts = new CancellationTokenSource();
            var response = await _agentService.SendMessageAsync(userMessage, OnPartialMessage, _agentCts.Token);

            // 思考完成：填充回复内容、移除占位步骤。
            // 推理过程保持展开（用户要求展开在对话里），发送新消息时才自动折叠上一条
            MainThread.BeginInvokeOnMainThread(() =>
            {
                assistantMsg.Content = BuildAssistantMessage(response);
                assistantMsg.Songs = response.Songs;
                assistantMsg.ReasoningContent = response.ReasoningContent;
                assistantMsg.IsThinking = false;
                // 移除"正在思考"占位项（如果有工具调用，工具步骤已追加在后面，只移除第一项占位）
                if (assistantMsg.ThinkingSteps.Count > 0 && assistantMsg.ThinkingSteps[0].StartsWith("💭"))
                    assistantMsg.ThinkingSteps.RemoveAt(0);
            });

            _ = SaveChatMessageSafeAsync(new ChatMessageRecord { Role = "assistant", Content = assistantMsg.Content, Timestamp = DateTime.UtcNow });
            _chatMemoryService.RecordMessage(assistantMsg);
            _ = TriggerMemoryExtractionAsync();
        }
        catch (OperationCanceledException)
        {
            // 用户点击中断按钮：保留已生成的部分内容并提示已停止
            MainThread.BeginInvokeOnMainThread(() =>
            {
                assistantMsg.Content = string.IsNullOrWhiteSpace(assistantMsg.Content)
                    ? "已停止回复喵。"
                    : assistantMsg.Content + "\n\n（已停止回复）";
                assistantMsg.IsThinking = false;
                if (assistantMsg.ThinkingSteps.Count > 0 && assistantMsg.ThinkingSteps[0].StartsWith("💭"))
                    assistantMsg.ThinkingSteps.RemoveAt(0);
                assistantMsg.IsThinkingExpanded = false;
            });
            _ = SaveChatMessageSafeAsync(new ChatMessageRecord { Role = "assistant", Content = assistantMsg.Content, Timestamp = DateTime.UtcNow });
            _chatMemoryService.RecordMessage(assistantMsg);
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                assistantMsg.Content = $"出错了喵：{ex.Message}";
                assistantMsg.IsThinking = false;
                if (assistantMsg.ThinkingSteps.Count > 0 && assistantMsg.ThinkingSteps[0].StartsWith("💭"))
                    assistantMsg.ThinkingSteps.RemoveAt(0);
                assistantMsg.IsThinkingExpanded = false;
            });
            _ = SaveChatMessageSafeAsync(new ChatMessageRecord { Role = "assistant", Content = assistantMsg.Content, Timestamp = DateTime.UtcNow });
            _chatMemoryService.RecordMessage(assistantMsg);
            _ = TriggerMemoryExtractionAsync();
        }
        finally
        {
            IsAgentThinking = false;
            _agentCts?.Dispose();
            _agentCts = null;
        }
    }

    /// <summary>请求滚动到最新消息的事件（供页面订阅）</summary>
    public event EventHandler? ScrollToLatestMessageRequested;

    /// <summary>裁剪旧聊天记录，只保留最近1000条</summary>
    private async Task TrimOldChatMessagesAsync()
    {
        try
        {
            var count = await _database.GetChatMessageCountAsync();
            if (count > 1000)
            {
                await _database.TrimChatMessagesAsync(1000);
            }
        }
        catch { }
    }

    /// <summary>安全保存聊天记录：捕获异常避免 DB 错误打断聊天流程</summary>
    /// <param name="record">待保存的聊天消息记录</param>
    private async Task SaveChatMessageSafeAsync(ChatMessageRecord record)
    {
        try { await _database.SaveChatMessageAsync(record); }
        catch (Exception ex) { Log.Debug("SearchViewModel", $"保存聊天记录失败: {ex.Message}"); }
    }

    /// <summary>触发AI记忆提取（后台异步，不阻塞UI）</summary>
    private async Task TriggerMemoryExtractionAsync()
    {
        if (!_agentService.IsConfigured) return;

        try
        {
            await Task.Delay(2000);
            await _chatMemoryService.ForceMemoryExtractionAsync(async (sysPrompt, userPrompt) =>
            {
                return await _agentService.QuickAskAsync(sysPrompt, userPrompt);
            });
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] 记忆提取失败: {ex.Message}");
        }
    }

    /// <summary>Agent 中间消息回调：工具调用过程追加思考步骤；流式正文/思考增量实时更新占位气泡。
    /// 回调来自 HTTP 读取线程，需切回主线程更新 UI。</summary>
    private void OnPartialMessage(ChatMessage partial)
    {
        if (_currentThinkingMessage == null) return;

        // 流式思考过程增量：累积到 ReasoningContent（思考区实时显示）
        if (partial.Role == "assistant" && !string.IsNullOrEmpty(partial.ReasoningContent))
        {
            var delta = partial.ReasoningContent;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var msg = _currentThinkingMessage;
                if (msg == null) return;
                msg.ReasoningContent = (msg.ReasoningContent ?? "") + delta;
                // 首个思考增量到达时移除"正在思考"占位
                if (msg.ThinkingSteps.Count > 0 && msg.ThinkingSteps[0].StartsWith("💭"))
                    msg.ThinkingSteps.RemoveAt(0);
            });
        }
        // 流式正文增量：实时填入回复内容
        else if (partial.Role == "assistant" && !string.IsNullOrEmpty(partial.Content)
            && (partial.ToolCalls == null || partial.ToolCalls.Count == 0))
        {
            var delta = partial.Content;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var msg = _currentThinkingMessage;
                if (msg == null) return;
                msg.Content = (msg.Content ?? "") + delta;
            });
        }
        else if (partial.Role == "assistant" && partial.ToolCalls != null && partial.ToolCalls.Count > 0)
        {
            var toolNames = string.Join(", ", partial.ToolCalls.Select(tc => tc.Function?.Name ?? "?"));
            var step = $"🔧 调用工具: {toolNames}";
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _currentThinkingMessage?.ThinkingSteps.Add(step);
            });
        }
        else if (partial.Role == "tool" && !string.IsNullOrEmpty(partial.Name))
        {
            var step = $"✅ {partial.Name} 完成";
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _currentThinkingMessage?.ThinkingSteps.Add(step);
            });
        }
    }


    private static string BuildAssistantMessage(ChatMessage response)
    {
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            return response.Content;
        }

        if (response.Songs?.Count > 0)
        {
            return $"帮你找到 {response.Songs.Count} 首相关歌曲喵。";
        }

        return "处理完成喵。";
    }

}
