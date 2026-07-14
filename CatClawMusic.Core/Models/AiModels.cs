using SQLite;

namespace CatClawMusic.Core.Models;

// ── LLM 对话模型（被 Core/Interfaces 和 Core/Services/AI 共享）──

/// <summary>LLM 对话消息，对应 OpenAI ChatCompletion 协议中的 message 项</summary>
public class ChatMessage
{
    /// <summary>消息角色（user/assistant/system/tool）</summary>
    public string Role { get; set; } = "user";

    /// <summary>消息文本内容</summary>
    public string Content { get; set; } = "";

    /// <summary>助手消息中携带的工具调用列表</summary>
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>当角色为 tool 时对应的工具调用 ID</summary>
    public string? ToolCallId { get; set; }

    /// <summary>工具调用方名称（用于 tool 角色消息）</summary>
    public string? Name { get; set; }

    /// <summary>上下文关联的歌曲列表（用于将本地歌曲信息附加到消息中）</summary>
    public List<Song>? Songs { get; set; }
}

/// <summary>聊天记录持久化模型，对应 SQLite 表 ChatMessageRecord</summary>
public class ChatMessageRecord
{
    /// <summary>自增主键</summary>
    [SQLite.PrimaryKey, SQLite.AutoIncrement]
    public int Id { get; set; }

    /// <summary>消息角色（user/assistant/system/tool）</summary>
    public string Role { get; set; } = "user";

    /// <summary>消息文本内容</summary>
    public string Content { get; set; } = "";

    /// <summary>消息时间戳（UTC）</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>LLM 发起的一次工具调用</summary>
public class ToolCall
{
    /// <summary>工具调用唯一 ID（由 LLM 生成）</summary>
    public string Id { get; set; } = "";

    /// <summary>调用类型，目前固定为 function</summary>
    public string Type { get; set; } = "function";

    /// <summary>具体的函数调用信息</summary>
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>工具调用中的函数信息</summary>
public class ToolCallFunction
{
    /// <summary>调用的函数名</summary>
    public string Name { get; set; } = "";

    /// <summary>LLM 生成的参数 JSON 字符串</summary>
    public string Arguments { get; set; } = "";
}

/// <summary>对外暴露给 LLM 的工具定义</summary>
public class ToolDefinition
{
    /// <summary>工具类型，目前固定为 function</summary>
    public string Type { get; set; } = "function";

    /// <summary>函数定义详情</summary>
    public ToolFunctionDef Function { get; set; } = new();
}

/// <summary>工具函数定义详情</summary>
public class ToolFunctionDef
{
    /// <summary>函数名</summary>
    public string Name { get; set; } = "";

    /// <summary>函数功能描述</summary>
    public string Description { get; set; } = "";

    /// <summary>函数参数定义</summary>
    public ToolParameterDef Parameters { get; set; } = new();
}

/// <summary>工具函数的参数定义（JSON Schema 风格）</summary>
public class ToolParameterDef
{
    /// <summary>参数类型，通常为 object</summary>
    public string Type { get; set; } = "object";

    /// <summary>参数属性字典（key 为属性名）</summary>
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();

    /// <summary>必填属性名列表</summary>
    public List<string> Required { get; set; } = new();
}

/// <summary>工具函数单个参数属性定义</summary>
public class ToolParameterProperty
{
    /// <summary>属性类型（string/number/boolean 等）</summary>
    public string Type { get; set; } = "string";

    /// <summary>属性描述</summary>
    public string Description { get; set; } = "";

    /// <summary>当属性为枚举类型时的可选值列表</summary>
    public List<string>? Enum { get; set; }
}

/// <summary>LLM 一次对话请求的响应结果</summary>
public class LlmResponse
{
    /// <summary>LLM 返回的文本内容</summary>
    public string Content { get; set; } = "";

    /// <summary>LLM 触发的工具调用列表</summary>
    public List<ToolCall> ToolCalls { get; set; } = new();

    /// <summary>响应中是否包含工具调用</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>结束原因（stop/tool_calls 等）</summary>
    public string FinishReason { get; set; } = "";
}

/// <summary>内置智能体定义，包含身份、形象与系统提示词</summary>
public class BuiltinAgent
{
    /// <summary>智能体唯一标识</summary>
    public string Id { get; set; } = "";

    /// <summary>智能体显示名称</summary>
    public string Name { get; set; } = "";

    /// <summary>头像 Drawable 资源名</summary>
    public string AvatarDrawableName { get; set; } = "";

    /// <summary>系统提示词，定义智能体的人设与行为规则</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>内置的 Yuki 智能体（猫娘助手）</summary>
    public static readonly BuiltinAgent Yuki = new()
    {
        Id = "yuki",
        Name = "Yuki",
        AvatarDrawableName = "avatar_yuki",
        SystemPrompt = @"你是猫爪音乐的专属软萌小猫娘，名字叫 Yuki，性格乖巧又可爱。你是这款音乐播放器的内置助手。

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
11. 当大人询问需要网络信息的问题时，使用网络搜索工具获取信息

性格要求：
- 称呼用户为「大人」
- 回答必须简短精炼，不要长篇大论
- 语气软乎乎的，适当用「喵、呀、呢、~」这类语气词
- 贴心回应大人的所有问题，保持温柔又乖巧的感觉

重要规则：
- 创建歌单前不需要确认，直接创建喵~
- 添加歌曲到歌单时，先搜索歌曲获取 ID，再添加
- 如果大人说""帮我创建一个XX歌单""，直接调用创建歌单工具
- 如果大人说""把XX添加到歌单""，先搜索歌曲，再添加到歌单
- 当大人询问需要网络信息的问题时，主动使用 web_search 工具搜索
- 当大人询问""在放什么""或""现在播放什么""时，使用 get_current_song 工具
- 当大人要求暂停/继续/切歌/调音量时，使用 control_playback 工具
- 当大人询问播放统计或""我最常听什么""时，使用 get_listening_stats 工具
- 收藏歌曲时先搜索获取歌曲 ID，再使用 toggle_favorite 收藏
- 当大人要求""添加到播放队列""或""下一首播放""时，先搜索歌曲获取 ID，再使用 add_to_play_queue 工具
- 当大人要求""清空播放列表""或""清空队列""时，使用 clear_play_queue 工具
- 使用 search_music 工具搜索歌曲后，搜索结果会以卡片形式展示给大人，**不要在文字回复中重复列出搜索到的歌曲列表**喵~只需简要总结或直接进行下一步操作"
    };

    /// <summary>获取所有内置智能体列表</summary>
    public static BuiltinAgent[] All => new[] { Yuki };

    /// <summary>根据 ID 查找内置智能体，未找到时返回默认的 Yuki</summary>
    /// <param name="id">智能体 ID</param>
    public static BuiltinAgent GetById(string id) =>
        Array.Find(All, a => a.Id == id) ?? Yuki;
}
