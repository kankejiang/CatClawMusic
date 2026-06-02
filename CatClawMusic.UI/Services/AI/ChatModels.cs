namespace CatClawMusic.UI.Services.AI;

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public List<CatClawMusic.Core.Models.Song>? Songs { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "function";
    public ToolCallFunction Function { get; set; } = new();
}

public class ToolCallFunction
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public ToolFunctionDef Function { get; set; } = new();
}

public class ToolFunctionDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ToolParameterDef Parameters { get; set; } = new();
}

public class ToolParameterDef
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class ToolParameterProperty
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public List<string>? Enum { get; set; }
}

public class LlmConfig
{
    public string Name { get; set; } = "默认配置";
    public string Provider { get; set; } = "deepseek";
    public string ApiUrl { get; set; } = "https://api.deepseek.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
    public bool Enabled { get; set; }
}

public class LlmConfigEntry : LlmConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            var providers = AgentService.GetProviders();
            var provider = Array.Find(providers, p => p.Id == Provider);
            return provider?.Name ?? Provider;
        }
    }
}

public class LlmResponse
{
    public string Content { get; set; } = "";
    public List<ToolCall> ToolCalls { get; set; } = new();
    public bool HasToolCalls => ToolCalls.Count > 0;
    public string FinishReason { get; set; } = "";
}

public class LlmProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultApiUrl { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string[] PresetModels { get; set; } = Array.Empty<string>();
}

public class BuiltinAgent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AvatarDrawableName { get; set; } = "";
    public string SystemPrompt { get; set; } = "";

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

    public static BuiltinAgent[] All => new[] { Yuki };

    public static BuiltinAgent GetById(string id) =>
        Array.Find(All, a => a.Id == id) ?? Yuki;
}
