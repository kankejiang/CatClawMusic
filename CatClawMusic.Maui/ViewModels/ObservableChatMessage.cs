using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 聊天消息的可观察包装类：在 <see cref="ChatMessage"/> 基础上增加思考过程步骤集合与展开状态，
/// 用于在聊天气泡内部展示可折叠的思考/工具调用过程。继承 <see cref="ObservableObject"/> 以支持属性变更通知。
/// </summary>
public partial class ObservableChatMessage : ObservableObject
{
    /// <summary>消息角色（user/assistant/system/tool）</summary>
    [ObservableProperty]
    private string _role = "user";

    /// <summary>消息文本内容</summary>
    [ObservableProperty]
    private string _content = "";

    /// <summary>助手消息中携带的工具调用列表</summary>
    [ObservableProperty]
    private List<ToolCall>? _toolCalls;

    /// <summary>当角色为 tool 时对应的工具调用 ID</summary>
    [ObservableProperty]
    private string? _toolCallId;

    /// <summary>工具调用方名称（用于 tool 角色消息）</summary>
    [ObservableProperty]
    private string? _name;

    /// <summary>上下文关联的歌曲列表（用于将本地歌曲信息附加到消息中）</summary>
    [ObservableProperty]
    private List<Song>? _songs;

    /// <summary>是否携带歌曲结果（用于展示可点击播放的歌曲卡片）</summary>
    public bool HasSongs => Songs != null && Songs.Count > 0;

    /// <summary>Songs 变更时同步通知 HasSongs</summary>
    partial void OnSongsChanged(List<Song>? value)
    {
        OnPropertyChanged(nameof(HasSongs));
    }

    /// <summary>本条消息关联的思考过程步骤列表（如"调用工具 xxx"、"xxx 完成"）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _thinkingSteps = new();

    /// <summary>本条消息的思考过程是否展开（点击切换）</summary>
    [ObservableProperty]
    private bool _isThinkingExpanded;

    /// <summary>是否正在思考（控制旋转指示器显示）</summary>
    [ObservableProperty]
    private bool _isThinking;

    /// <summary>推理模型的思考内容（reasoning_content），展示在思考区</summary>
    [ObservableProperty]
    private string? _reasoningContent;

    /// <summary>是否有思考步骤可展示</summary>
    public bool HasThinkingSteps => ThinkingSteps.Count > 0;

    /// <summary>是否有思考内容可展示（推理文本或工具步骤），控制思考区整体可见性</summary>
    public bool HasThinking => ThinkingSteps.Count > 0 || !string.IsNullOrWhiteSpace(ReasoningContent);

    /// <summary>是否有推理思考文本</summary>
    public bool HasReasoning => !string.IsNullOrWhiteSpace(ReasoningContent);

    /// <summary>展开且有推理内容时显示推理文本</summary>
    public bool ShowReasoning => IsThinkingExpanded && HasReasoning;

    /// <summary>思考过程摘要文本（折叠时显示，根据 IsThinking 和步骤数自动计算）</summary>
    public string ThinkingSummary => IsThinking
        ? "思考中..."
        : (ThinkingSteps.Count > 0 ? $"已完成 · {ThinkingSteps.Count} 个步骤" : (HasReasoning ? "已深度思考" : ""));

    /// <summary>默认构造：订阅初始集合的变更事件</summary>
    public ObservableChatMessage()
    {
        ThinkingSteps.CollectionChanged += OnThinkingStepsCollectionChanged;
    }

    /// <summary>从 ChatMessage 复制字段构造</summary>
    public ObservableChatMessage(ChatMessage msg) : this()
    {
        Role = msg.Role;
        Content = msg.Content;
        ReasoningContent = msg.ReasoningContent;
        ToolCalls = msg.ToolCalls;
        ToolCallId = msg.ToolCallId;
        Name = msg.Name;
        Songs = msg.Songs;
    }

    /// <summary>允许隐式转换为 ChatMessage，便于传递给需要 ChatMessage 的服务</summary>
    public static implicit operator ChatMessage(ObservableChatMessage msg) => new()
    {
        Role = msg.Role,
        Content = msg.Content,
        ReasoningContent = msg.ReasoningContent,
        ToolCalls = msg.ToolCalls,
        ToolCallId = msg.ToolCallId,
        Name = msg.Name,
        Songs = msg.Songs
    };

    /// <summary>切换思考过程展开/折叠状态</summary>
    [RelayCommand]
    public void ToggleThinking()
    {
        IsThinkingExpanded = !IsThinkingExpanded;
    }

    /// <summary>ThinkingSteps 集合变更时同步通知 HasThinkingSteps 和 ThinkingSummary</summary>
    partial void OnThinkingStepsChanged(ObservableCollection<string> oldValue, ObservableCollection<string> newValue)
    {
        if (oldValue != null) oldValue.CollectionChanged -= OnThinkingStepsCollectionChanged;
        if (newValue != null) newValue.CollectionChanged += OnThinkingStepsCollectionChanged;
        OnPropertyChanged(nameof(HasThinkingSteps));
        OnPropertyChanged(nameof(ThinkingSummary));
    }

    /// <summary>IsThinking 变更时同步通知 ThinkingSummary</summary>
    partial void OnIsThinkingChanged(bool value)
    {
        OnPropertyChanged(nameof(ThinkingSummary));
    }

    /// <summary>ReasoningContent 变更时同步通知 HasThinking/HasReasoning/ThinkingSummary</summary>
    partial void OnReasoningContentChanged(string? value)
    {
        OnPropertyChanged(nameof(HasThinking));
        OnPropertyChanged(nameof(HasReasoning));
        OnPropertyChanged(nameof(ShowReasoning));
        OnPropertyChanged(nameof(ThinkingSummary));
    }

    /// <summary>IsThinkingExpanded 变更时同步通知 ShowReasoning</summary>
    partial void OnIsThinkingExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReasoning));
    }

    private void OnThinkingStepsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasThinkingSteps));
        OnPropertyChanged(nameof(ThinkingSummary));
    }
}
