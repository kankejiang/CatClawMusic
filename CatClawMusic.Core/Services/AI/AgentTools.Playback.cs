using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 搜索音乐库工具，按关键词（歌名、艺术家、专辑）检索本地与远程合并后的歌曲列表。
/// </summary>

public class PlaySongTool : IAgentTool
{
    /// <summary>音频播放器服务</summary>
    private readonly IAudioPlayerService _player;
    /// <summary>音乐库服务，用于获取合并歌曲列表</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>播放队列，用于设置播放列表并选中歌曲</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>工具名称</summary>
    public string Name => "play_song";
    /// <summary>工具描述</summary>
    public string Description => "播放指定歌曲";

    /// <summary>
    /// 构造 PlaySongTool 实例
    /// </summary>
    /// <param name="player">音频播放器服务</param>
    /// <param name="musicLibrary">音乐库服务</param>
    /// <param name="playQueue">播放队列</param>
    public PlaySongTool(IAudioPlayerService player, IMusicLibraryService musicLibrary, PlayQueue playQueue)
    {
        _player = player;
        _musicLibrary = musicLibrary;
        _playQueue = playQueue;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["song_id"] = new() { Type = "integer", Description = "要播放的歌曲 ID" }
                },
                Required = new List<string> { "song_id" }
            }
        }
    };

    /// <summary>
    /// 执行播放歌曲操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 song_id 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");
        if (songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌曲 ID" });

        var allSongs = await _musicLibrary.GetMergedSongsAsync();
        var song = allSongs.FirstOrDefault(s => s.Id == songId);
        if (song == null) return JsonSerializer.Serialize(new { error = "未找到该歌曲" });

        _playQueue.SetSongs(allSongs);
        _playQueue.SelectSong(songId);
        await _player.PlayAsync(song.FilePath);
        return JsonSerializer.Serialize(new { success = true, message = $"正在播放「{song.Title}」- {song.Artist}" });
    }
}

public class ControlPlaybackTool : IAgentTool
{
    /// <summary>音频播放器服务</summary>
    private readonly IAudioPlayerService _player;
    /// <summary>播放队列，用于切换上一首/下一首</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>工具名称</summary>
    public string Name => "control_playback";
    /// <summary>工具描述</summary>
    public string Description => "控制音乐播放，支持暂停、恢复、下一首、上一首、停止、调节音量、跳转到指定位置";

    /// <summary>
    /// 构造 ControlPlaybackTool 实例
    /// </summary>
    /// <param name="player">音频播放器服务</param>
    /// <param name="playQueue">播放队列</param>
    public ControlPlaybackTool(IAudioPlayerService player, PlayQueue playQueue)
    {
        _player = player;
        _playQueue = playQueue;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["action"] = new() { Type = "string", Description = "操作类型：pause(暂停), resume(恢复), next(下一首), previous(上一首), stop(停止)", Enum = new List<string> { "pause", "resume", "next", "previous", "stop" } },
                    ["volume"] = new() { Type = "integer", Description = "音量 0-100，仅当 action 不指定时使用" },
                    ["seek_to"] = new() { Type = "integer", Description = "跳转到指定秒数" }
                },
                Required = new List<string> { "action" }
            }
        }
    };

    /// <summary>
    /// 执行播放控制操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 action、可选 volume、seek_to 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 或 error 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var action = ArgHelper.ExtractStringArgFallback(arguments, "action");

        try
        {
            switch (action)
            {
                case "pause":
                    if (_player.IsPlaying) await _player.PauseAsync();
                    return JsonSerializer.Serialize(new { success = true, message = "已暂停播放" });
                case "resume":
                    if (!string.IsNullOrEmpty(_player.CurrentSongFilePath))
                    {
                        if (!_player.IsPlaying)
                            await _player.ResumeAsync();
                    }
                    return JsonSerializer.Serialize(new { success = true, message = "已恢复播放" });
                case "next":
                    var nextSong = _playQueue.Next();
                    if (nextSong != null)
                        await _player.PlayAsync(nextSong.FilePath);
                    return JsonSerializer.Serialize(new { success = true, message = nextSong != null ? $"正在播放下一首「{nextSong.Title}」" : "播放队列为空" });
                case "previous":
                    var prevSong = _playQueue.Previous();
                    if (prevSong != null)
                        await _player.PlayAsync(prevSong.FilePath);
                    return JsonSerializer.Serialize(new { success = true, message = prevSong != null ? $"正在播放上一首「{prevSong.Title}」" : "没有上一首" });
                case "stop":
                    await _player.StopAsync();
                    return JsonSerializer.Serialize(new { success = true, message = "已停止播放" });
                default:
                    var volume = ArgHelper.ExtractIntArgFallback(arguments, "volume");
                    if (volume >= 0 && volume <= 100)
                    {
                        _player.Volume = volume;
                        return JsonSerializer.Serialize(new { success = true, message = $"音量已设置为 {volume}" });
                    }
                    var seekTo = ArgHelper.ExtractIntArgFallback(arguments, "seek_to");
                    if (seekTo >= 0)
                    {
                        await _player.SeekAsync(TimeSpan.FromSeconds(seekTo));
                        return JsonSerializer.Serialize(new { success = true, message = $"已跳转到 {seekTo} 秒" });
                    }
                    return JsonSerializer.Serialize(new { error = $"未知操作: {action}，支持 pause/resume/next/previous/stop" });
            }
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"播放控制失败: {ex.Message}" });
        }
    }
}

public class GetCurrentSongTool : IAgentTool
{
    /// <summary>播放队列，用于获取当前歌曲</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>音频播放器服务，用于获取播放状态与进度</summary>
    private readonly IAudioPlayerService _player;
    /// <summary>工具名称</summary>
    public string Name => "get_current_song";
    /// <summary>工具描述</summary>
    public string Description => "获取当前正在播放的歌曲信息，包括歌名、艺术家、专辑、播放进度等";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 GetCurrentSongTool 实例
    /// </summary>
    /// <param name="playQueue">播放队列</param>
    /// <param name="player">音频播放器服务</param>
    public GetCurrentSongTool(PlayQueue playQueue, IAudioPlayerService player)
    {
        _playQueue = playQueue;
        _player = player;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef()
        }
    };

    /// <summary>
    /// 执行获取当前播放歌曲信息操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串（该工具无参数）</param>
    /// <returns>JSON 序列化结果，包含 is_playing 与 song 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var current = _playQueue.CurrentSong;
        if (current == null)
            return JsonSerializer.Serialize(new { error = "当前没有正在播放的歌曲", is_playing = false });

        return JsonSerializer.Serialize(new
        {
            is_playing = _player.IsPlaying,
            song = new
            {
                current.Id, current.Title, current.Artist, current.Album,
                Duration = _player.Duration,
                Position = _player.CurrentPosition
            }
        });
    }
}

public class GetPlayQueueTool : IAgentTool
{
    /// <summary>播放队列</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>工具名称</summary>
    public string Name => "get_play_queue";
    /// <summary>工具描述</summary>
    public string Description => "获取当前播放队列信息，包括播放模式、队列中的歌曲和即将播放的歌曲";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 GetPlayQueueTool 实例
    /// </summary>
    /// <param name="playQueue">播放队列</param>
    public GetPlayQueueTool(PlayQueue playQueue) => _playQueue = playQueue;

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef()
        }
    };

    /// <summary>
    /// 执行获取播放队列信息操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串（该工具无参数）</param>
    /// <returns>JSON 序列化结果，包含 play_mode、total_songs、current_song、upcoming 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        try
        {
            var current = _playQueue.CurrentSong;
            var songs = _playQueue.GetSongs();
            var upcoming = _playQueue.GetUpcomingSongs(5);
            var modeName = _playQueue.PlayMode switch
            {
                PlayMode.Sequential => "顺序播放",
                PlayMode.Shuffle => "随机播放",
                PlayMode.SingleRepeat => "单曲循环",
                PlayMode.ListRepeat => "列表循环",
                _ => "未知"
            };

            return JsonSerializer.Serialize(new
            {
                play_mode = modeName,
                total_songs = songs.Count,
                current_song = current != null ? new { current.Id, current.Title, current.Artist } : null,
                upcoming = upcoming.Select(s => new { s.Id, s.Title, s.Artist }).ToList()
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"获取播放队列失败: {ex.Message}" });
        }
    }
}

public class ToggleFavoriteTool : IAgentTool
{
    /// <summary>收藏切换回调，参数为歌曲 ID 与是否收藏</summary>
    private readonly Func<int, bool, Task> _toggleFavorite;
    /// <summary>工具名称</summary>
    public string Name => "toggle_favorite";
    /// <summary>工具描述</summary>
    public string Description => "收藏或取消收藏一首歌曲";

    /// <summary>
    /// 构造 ToggleFavoriteTool 实例
    /// </summary>
    /// <param name="toggleFavorite">收藏切换回调函数</param>
    public ToggleFavoriteTool(Func<int, bool, Task> toggleFavorite)
    {
        _toggleFavorite = toggleFavorite;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["song_id"] = new() { Type = "integer", Description = "歌曲 ID" },
                    ["favorite"] = new() { Type = "boolean", Description = "true=收藏, false=取消收藏" }
                },
                Required = new List<string> { "song_id", "favorite" }
            }
        }
    };

    /// <summary>
    /// 执行切换收藏状态操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 song_id 与 favorite 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");
        var favStr = ArgHelper.ExtractStringArgFallback(arguments, "favorite");
        var favorite = favStr?.ToLower() != "false" && favStr != "0";

        if (songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌曲 ID" });

        try
        {
            await _toggleFavorite(songId, favorite);
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = favorite ? "已收藏歌曲" : "已取消收藏"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"操作失败: {ex.Message}" });
        }
    }
}

public class AddToPlayQueueTool : IAgentTool
{
    /// <summary>播放队列</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>音频播放器服务</summary>
    private readonly IAudioPlayerService _player;
    /// <summary>工具名称</summary>
    public string Name => "add_to_play_queue";
    /// <summary>工具描述</summary>
    public string Description => "将歌曲添加到播放队列，可以添加到下一首播放或添加到队列末尾";

    /// <summary>
    /// 构造 AddToPlayQueueTool 实例
    /// </summary>
    /// <param name="playQueue">播放队列</param>
    /// <param name="musicLibrary">音乐库服务</param>
    /// <param name="player">音频播放器服务</param>
    public AddToPlayQueueTool(PlayQueue playQueue, IMusicLibraryService musicLibrary, IAudioPlayerService player)
    {
        _playQueue = playQueue;
        _musicLibrary = musicLibrary;
        _player = player;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["song_id"] = new() { Type = "integer", Description = "要添加的歌曲 ID" },
                    ["position"] = new() { Type = "string", Description = "添加位置：next(下一首播放) 或 end(队列末尾)，默认 next", Enum = new List<string> { "next", "end" } }
                },
                Required = new List<string> { "song_id" }
            }
        }
    };

    /// <summary>
    /// 执行添加歌曲到播放队列操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 song_id 与可选 position（next/end，默认 next）字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");
        if (songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌曲 ID" });

        var position = ArgHelper.ExtractStringArgFallback(arguments, "position") ?? "next";

        try
        {
            var allSongs = await _musicLibrary.GetMergedSongsAsync();
            var song = allSongs.FirstOrDefault(s => s.Id == songId);
            if (song == null) return JsonSerializer.Serialize(new { error = "未找到该歌曲" });

            if (position == "end")
                _playQueue.AddToEnd(song);
            else
                _playQueue.AddNext(song);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = position == "end"
                    ? $"已将「{song.Title}」添加到播放队列末尾"
                    : $"已将「{song.Title}」设为下一首播放"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"添加到播放队列失败: {ex.Message}" });
        }
    }
}

public class ClearPlayQueueTool : IAgentTool
{
    /// <summary>播放队列</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>音频播放器服务</summary>
    private readonly IAudioPlayerService _player;
    /// <summary>工具名称</summary>
    public string Name => "clear_play_queue";
    /// <summary>工具描述</summary>
    public string Description => "清空播放队列并停止播放";

    /// <summary>
    /// 构造 ClearPlayQueueTool 实例
    /// </summary>
    /// <param name="playQueue">播放队列</param>
    /// <param name="player">音频播放器服务</param>
    public ClearPlayQueueTool(PlayQueue playQueue, IAudioPlayerService player)
    {
        _playQueue = playQueue;
        _player = player;
    }

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义
    /// </summary>
    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef()
        }
    };

    /// <summary>
    /// 执行清空播放队列操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串（该工具无参数）</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        try
        {
            await _player.StopAsync();
            _playQueue.SetSongs(Array.Empty<Song>());
            return JsonSerializer.Serialize(new { success = true, message = "播放队列已清空" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"清空播放队列失败: {ex.Message}" });
        }
    }
}
