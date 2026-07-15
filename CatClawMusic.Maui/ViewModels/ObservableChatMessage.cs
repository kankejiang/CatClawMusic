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

    /// <summary>本条消息关联的思考过程步骤列表（如"调用工具 xxx"、"xxx 完成"）</summary>
    [ObservableProperty]
    private ObservableCollection<string> _thinkingSteps = new();

    /// <summary>本条消息的思考过程是否展开（点击切换）</summary>
    [ObservableProperty]
    private bool _isThinkingExpanded;

    /// <summary>是否正在思考（控制旋转指示器显示）</summary>
    [ObservableProperty]
    private bool _isThinking;

    /// <summary>是否有思考步骤可展示</summary>
    public bool HasThinkingSteps => ThinkingSteps.Count > 0;

    /// <summary>思考过程摘要文本（折叠时显示，根据 IsThinking 和步骤数自动计算）</summary>
    public string ThinkingSummary => IsThinking
        ? "思考中..."
        : (ThinkingSteps.Count > 0 ? $"已完成 · {ThinkingSteps.Count} 个步骤" : "");

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

    private void OnThinkingStepsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasThinkingSteps));
        OnPropertyChanged(nameof(ThinkingSummary));
    }
}
