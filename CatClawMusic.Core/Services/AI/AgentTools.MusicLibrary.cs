using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 搜索音乐库工具，按关键词（歌名、艺术家、专辑）检索本地与远程合并后的歌曲列表。
/// </summary>

public class SearchMusicTool : IAgentTool
{
    /// <summary>音乐库服务，用于执行歌曲搜索</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称（LLM 调用时使用的 function name）</summary>
    public string Name => "search_music";
    /// <summary>工具描述，提供给 LLM 用于判断何时调用该工具</summary>
    public string Description => "搜索音乐库中的歌曲，支持按歌名、艺术家、专辑关键词搜索";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 SearchMusicTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public SearchMusicTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

    /// <summary>
    /// 返回该工具的 OpenAI 兼容函数定义（参数 schema）
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
                    ["keyword"] = new() { Type = "string", Description = "搜索关键词，可以是歌名、艺术家或专辑名" }
                },
                Required = new List<string> { "keyword" }
            }
        }
    };

    /// <summary>
    /// 执行搜索操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 keyword 字段</param>
    /// <returns>JSON 序列化结果，包含 count 与 songs 列表</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var keyword = ArgHelper.ExtractStringArgFallback(arguments, "keyword");
        if (string.IsNullOrWhiteSpace(keyword)) return JsonSerializer.Serialize(new { error = "请提供搜索关键词" });

        var songs = await _musicLibrary.SearchAsync(keyword);
        var results = songs.Take(20).Select(s => new
        {
            s.Id, s.Title, s.Artist, s.Album, s.Duration
        }).ToList();

        return JsonSerializer.Serialize(new { count = results.Count, songs = results });
    }
}

public class CreatePlaylistTool : IAgentTool
{
    /// <summary>音乐库服务，用于执行歌单创建</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "create_playlist";
    /// <summary>工具描述</summary>
    public string Description => "创建新的播放列表（歌单）";

    /// <summary>
    /// 构造 CreatePlaylistTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public CreatePlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["name"] = new() { Type = "string", Description = "歌单名称" }
                },
                Required = new List<string> { "name" }
            }
        }
    };

    /// <summary>
    /// 执行创建歌单操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 name 字段</param>
    /// <returns>JSON 序列化结果，包含 success、playlistId、playlistName、message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var name = ArgHelper.ExtractStringArgFallback(arguments, "name");
        if (string.IsNullOrWhiteSpace(name)) return JsonSerializer.Serialize(new { error = "请提供歌单名称" });

        var id = await _musicLibrary.CreatePlaylistAsync(name);
        return JsonSerializer.Serialize(new { success = true, playlistId = id, playlistName = name, message = $"歌单「{name}」已创建" });
    }
}

public class AddSongToPlaylistTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "add_song_to_playlist";
    /// <summary>工具描述</summary>
    public string Description => "将歌曲添加到指定歌单中";

    /// <summary>
    /// 构造 AddSongToPlaylistTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public AddSongToPlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["playlist_id"] = new() { Type = "integer", Description = "目标歌单 ID" },
                    ["song_id"] = new() { Type = "integer", Description = "要添加的歌曲 ID" }
                },
                Required = new List<string> { "playlist_id", "song_id" }
            }
        }
    };

    /// <summary>
    /// 执行添加歌曲到歌单操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 playlist_id 与 song_id 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        var songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");

        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });
        if (songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌曲 ID" });

        await _musicLibrary.AddSongToPlaylistAsync(playlistId, songId);
        return JsonSerializer.Serialize(new { success = true, message = "歌曲已添加到歌单" });
    }
}

public class RemoveSongFromPlaylistTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "remove_song_from_playlist";
    /// <summary>工具描述</summary>
    public string Description => "从指定歌单中移除歌曲";

    /// <summary>
    /// 构造 RemoveSongFromPlaylistTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public RemoveSongFromPlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["playlist_id"] = new() { Type = "integer", Description = "歌单 ID" },
                    ["song_id"] = new() { Type = "integer", Description = "要移除的歌曲 ID" }
                },
                Required = new List<string> { "playlist_id", "song_id" }
            }
        }
    };

    /// <summary>
    /// 执行从歌单移除歌曲操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 playlist_id 与 song_id 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        var songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");

        if (playlistId <= 0 || songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID 和歌曲 ID" });

        await _musicLibrary.RemoveSongFromPlaylistAsync(playlistId, songId);
        return JsonSerializer.Serialize(new { success = true, message = "歌曲已从歌单中移除" });
    }
}

public class ListPlaylistsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "list_playlists";
    /// <summary>工具描述</summary>
    public string Description => "获取用户所有播放列表（歌单）";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 ListPlaylistsTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public ListPlaylistsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
    /// 执行列出所有歌单操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串（该工具无参数）</param>
    /// <returns>JSON 序列化结果，包含 count 与 playlists 列表</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlists = await _musicLibrary.GetAllPlaylistsAsync();
        var results = playlists.Select(p => new { p.Id, p.Name, p.SongCount }).ToList();
        return JsonSerializer.Serialize(new { count = results.Count, playlists = results });
    }
}

public class GetPlaylistSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_playlist_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取指定歌单中的歌曲列表";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 GetPlaylistSongsTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public GetPlaylistSongsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["playlist_id"] = new() { Type = "integer", Description = "歌单 ID" }
                },
                Required = new List<string> { "playlist_id" }
            }
        }
    };

    /// <summary>
    /// 执行获取歌单歌曲列表操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 playlist_id 字段</param>
    /// <returns>JSON 序列化结果，包含 count 与 songs 列表</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });

        var songs = await _musicLibrary.GetPlaylistSongsAsync(playlistId);
        var results = songs.Select(s => new { s.Id, s.Title, s.Artist, s.Album }).ToList();
        return JsonSerializer.Serialize(new { count = results.Count, songs = results });
    }
}

public class DeletePlaylistTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "delete_playlist";
    /// <summary>工具描述</summary>
    public string Description => "删除指定的播放列表（歌单）";

    /// <summary>
    /// 构造 DeletePlaylistTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public DeletePlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["playlist_id"] = new() { Type = "integer", Description = "要删除的歌单 ID" }
                },
                Required = new List<string> { "playlist_id" }
            }
        }
    };

    /// <summary>
    /// 执行删除歌单操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 playlist_id 字段</param>
    /// <returns>JSON 序列化结果，包含 success 与 message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });

        await _musicLibrary.DeletePlaylistAsync(playlistId);
        return JsonSerializer.Serialize(new { success = true, message = "歌单已删除" });
    }
}

public class GetFavoriteSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_favorite_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取收藏的歌曲列表";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 GetFavoriteSongsTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public GetFavoriteSongsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["limit"] = new() { Type = "integer", Description = "最多返回多少首，默认 20" }
                }
            }
        }
    };

    /// <summary>
    /// 执行获取收藏歌曲列表操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，可选 limit 字段（默认 20）</param>
    /// <returns>JSON 序列化结果，包含 count 与 songs 列表</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var limit = ArgHelper.ExtractIntArgFallback(arguments, "limit");
        if (limit <= 0) limit = 20;

        try
        {
            var songs = await _musicLibrary.GetFavoriteSongsAsync();
            var results = songs.Take(limit).Select(s => new
            {
                s.Id, s.Title, s.Artist, s.Album, s.Duration
            }).ToList();
            return JsonSerializer.Serialize(new { count = results.Count, songs = results });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"获取收藏列表失败: {ex.Message}" });
        }
    }
}

public class GetRecentSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_recent_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取最近播放的歌曲列表";
    public bool IsReadOnly => true;

    /// <summary>
    /// 构造 GetRecentSongsTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public GetRecentSongsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["limit"] = new() { Type = "integer", Description = "最多返回多少首，默认 20" }
                }
            }
        }
    };

    /// <summary>
    /// 执行获取最近播放歌曲操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，可选 limit 字段（默认 20）</param>
    /// <returns>JSON 序列化结果，包含 count 与 songs 列表</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var limit = ArgHelper.ExtractIntArgFallback(arguments, "limit");
        if (limit <= 0) limit = 20;

        try
        {
            var songs = await _musicLibrary.GetRecentSongsAsync();
            var results = songs.Take(limit).Select(s => new
            {
                s.Id, s.Title, s.Artist, s.Album, s.Duration
            }).ToList();
            return JsonSerializer.Serialize(new { count = results.Count, songs = results });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"获取最近播放失败: {ex.Message}" });
        }
    }
}

public class GetListeningStatsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_listening_stats";
    /// <summary>工具描述</summary>
    public bool IsReadOnly => true;
    public string Description => "获取播放统计数据，包括播放次数最多的歌曲排行";

    /// <summary>
    /// 构造 GetListeningStatsTool 实例
    /// </summary>
    /// <param name="musicLibrary">音乐库服务</param>
    public GetListeningStatsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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
                    ["top_n"] = new() { Type = "integer", Description = "排行榜前几名，默认 10" }
                }
            }
        }
    };

    /// <summary>
    /// 执行获取播放统计操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，可选 top_n 字段（默认 10）</param>
    /// <returns>JSON 序列化结果，包含 top_n、total_plays、songs 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var topN = ArgHelper.ExtractIntArgFallback(arguments, "top_n");
        if (topN <= 0) topN = 10;

        try
        {
            var topSongs = await _musicLibrary.GetTopPlayedSongsAsync(topN);
            var results = topSongs.Select(s => new
            {
                s.Id, s.Title, s.Artist, s.PlayCount
            }).ToList();

            var total = topSongs.Sum(s => s.PlayCount);
            return JsonSerializer.Serialize(new
            {
                top_n = topN,
                total_plays = total,
                songs = results
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"获取统计数据失败: {ex.Message}" });
        }
    }
}
