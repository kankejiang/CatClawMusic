using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Services.AI;

public class SearchMusicTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "search_music";
    public string Description => "搜索音乐库中的歌曲，支持按歌名、艺术家、专辑关键词搜索";

    public SearchMusicTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var keyword = NativeInterop.AiExtractStringArg(arguments, "keyword")
            ?? ArgHelper.ExtractStringArgFallback(arguments, "keyword");
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
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "create_playlist";
    public string Description => "创建新的播放列表（歌单）";

    public CreatePlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var name = NativeInterop.AiExtractStringArg(arguments, "name")
            ?? ArgHelper.ExtractStringArgFallback(arguments, "name");
        if (string.IsNullOrWhiteSpace(name)) return JsonSerializer.Serialize(new { error = "请提供歌单名称" });

        var id = await _musicLibrary.CreatePlaylistAsync(name);
        return JsonSerializer.Serialize(new { success = true, playlistId = id, playlistName = name, message = $"歌单「{name}」已创建" });
    }
}

public class AddSongToPlaylistTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "add_song_to_playlist";
    public string Description => "将歌曲添加到指定歌单中";

    public AddSongToPlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = NativeInterop.AiExtractIntArg(arguments, "playlist_id", 0);
        if (playlistId == 0) playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        var songId = NativeInterop.AiExtractIntArg(arguments, "song_id", 0);
        if (songId == 0) songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");

        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });
        if (songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌曲 ID" });

        await _musicLibrary.AddSongToPlaylistAsync(playlistId, songId);
        return JsonSerializer.Serialize(new { success = true, message = "歌曲已添加到歌单" });
    }
}

public class RemoveSongFromPlaylistTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "remove_song_from_playlist";
    public string Description => "从指定歌单中移除歌曲";

    public RemoveSongFromPlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = NativeInterop.AiExtractIntArg(arguments, "playlist_id", 0);
        if (playlistId == 0) playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        var songId = NativeInterop.AiExtractIntArg(arguments, "song_id", 0);
        if (songId == 0) songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");

        if (playlistId <= 0 || songId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID 和歌曲 ID" });

        await _musicLibrary.RemoveSongFromPlaylistAsync(playlistId, songId);
        return JsonSerializer.Serialize(new { success = true, message = "歌曲已从歌单中移除" });
    }
}

public class ListPlaylistsTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "list_playlists";
    public string Description => "获取用户所有播放列表（歌单）";

    public ListPlaylistsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef()
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlists = await _musicLibrary.GetAllPlaylistsAsync();
        var results = playlists.Select(p => new { p.Id, p.Name, p.SongCount }).ToList();
        return JsonSerializer.Serialize(new { count = results.Count, playlists = results });
    }
}

public class GetPlaylistSongsTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "get_playlist_songs";
    public string Description => "获取指定歌单中的歌曲列表";

    public GetPlaylistSongsTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = NativeInterop.AiExtractIntArg(arguments, "playlist_id", 0);
        if (playlistId == 0) playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });

        var songs = await _musicLibrary.GetPlaylistSongsAsync(playlistId);
        var results = songs.Select(s => new { s.Id, s.Title, s.Artist, s.Album }).ToList();
        return JsonSerializer.Serialize(new { count = results.Count, songs = results });
    }
}

public class DeletePlaylistTool : IAgentTool
{
    private readonly IMusicLibraryService _musicLibrary;
    public string Name => "delete_playlist";
    public string Description => "删除指定的播放列表（歌单）";

    public DeletePlaylistTool(IMusicLibraryService musicLibrary) => _musicLibrary = musicLibrary;

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var playlistId = NativeInterop.AiExtractIntArg(arguments, "playlist_id", 0);
        if (playlistId == 0) playlistId = ArgHelper.ExtractIntArgFallback(arguments, "playlist_id");
        if (playlistId <= 0) return JsonSerializer.Serialize(new { error = "请提供有效的歌单 ID" });

        await _musicLibrary.DeletePlaylistAsync(playlistId);
        return JsonSerializer.Serialize(new { success = true, message = "歌单已删除" });
    }
}

public class PlaySongTool : IAgentTool
{
    private readonly IAudioPlayerService _player;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly PlayQueue _playQueue;
    public string Name => "play_song";
    public string Description => "播放指定歌曲";

    public PlaySongTool(IAudioPlayerService player, IMusicLibraryService musicLibrary, PlayQueue playQueue)
    {
        _player = player;
        _musicLibrary = musicLibrary;
        _playQueue = playQueue;
    }

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

    public async Task<string> ExecuteAsync(string arguments)
    {
        var songId = NativeInterop.AiExtractIntArg(arguments, "song_id", 0);
        if (songId == 0) songId = ArgHelper.ExtractIntArgFallback(arguments, "song_id");
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

public class WebSearchTool : IAgentTool
{
    private readonly HttpClient _httpClient;
    public string Name => "web_search";
    public string Description => "在互联网上搜索信息，可以搜索新闻、知识、音乐资讯等内容";

    public WebSearchTool()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

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
                    ["query"] = new() { Type = "string", Description = "搜索关键词" }
                },
                Required = new List<string> { "query" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var query = NativeInterop.AiExtractStringArg(arguments, "query")
            ?? ArgHelper.ExtractStringArgFallback(arguments, "query");
        
        if (string.IsNullOrWhiteSpace(query)) 
            return JsonSerializer.Serialize(new { error = "请提供搜索关键词" });

        try
        {
            // 使用 DuckDuckGo 的 HTML 搜索页面（简单的替代方案）
            var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(searchUrl);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            
            // 简单解析搜索结果（截取一些内容）
            var results = ExtractSearchResults(html, query);
            
            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                query = query, 
                results = results,
                message = "搜索完成，已找到相关信息"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"网络搜索失败: {ex.Message}" });
        }
    }

    private List<object> ExtractSearchResults(string html, string query)
    {
        var results = new List<object>();
        
        // 简单的 HTML 解析，提取搜索结果
        // 注意：这只是一个基础实现，实际项目中建议使用 HTML 解析库
        try
        {
            var lines = html.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var resultCount = 0;
            var currentTitle = "";
            var currentUrl = "";
            var currentSnippet = "";
            
            foreach (var line in lines)
            {
                if (resultCount >= 5) break;
                
                if (line.Contains("class=\"result__a\""))
                {
                    // 提取 URL
                    var urlMatch = System.Text.RegularExpressions.Regex.Match(line, "href=\"([^\"]+)\"");
                    if (urlMatch.Success)
                    {
                        currentUrl = urlMatch.Groups[1].Value;
                        // 清理 URL
                        if (currentUrl.StartsWith("//")) currentUrl = "https:" + currentUrl;
                    }
                    
                    // 提取标题
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(line, ">([^<]+)</a>");
                    if (titleMatch.Success)
                    {
                        currentTitle = titleMatch.Groups[1].Value.Trim();
                    }
                }
                else if (line.Contains("class=\"result__snippet\""))
                {
                    // 提取摘要
                    var snippetMatch = System.Text.RegularExpressions.Regex.Match(line, ">([^<]+)</a>");
                    if (snippetMatch.Success)
                    {
                        currentSnippet = snippetMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        var simpleMatch = System.Text.RegularExpressions.Regex.Match(line, ">([^<]+)<");
                        if (simpleMatch.Success)
                        {
                            currentSnippet = simpleMatch.Groups[1].Value.Trim();
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(currentTitle) && !string.IsNullOrEmpty(currentUrl))
                    {
                        results.Add(new
                        {
                            title = currentTitle,
                            url = currentUrl,
                            snippet = currentSnippet
                        });
                        resultCount++;
                        
                        currentTitle = "";
                        currentUrl = "";
                        currentSnippet = "";
                    }
                }
            }
            
            // 如果没有找到结果，返回简单的提示
            if (results.Count == 0)
            {
                results.Add(new
                {
                    title = "搜索提示",
                    url = "",
                    snippet = $"已尝试搜索「{query}」，由于网页解析限制，建议用户直接使用浏览器查看详细结果"
                });
            }
        }
        catch
        {
            results.Add(new
            {
                title = "搜索提示",
                url = "",
                snippet = $"已尝试搜索「{query}」，网络搜索功能可用，但解析网页内容时遇到限制"
            });
        }
        
        return results;
    }
}

internal static class ArgHelper
{
    internal static string? ExtractStringArgFallback(string arguments, string key)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            return args?.TryGetValue(key, out var val) == true ? val.GetString() : null;
        }
        catch { return null; }
    }

    internal static int ExtractIntArgFallback(string arguments, string key)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            return args?.TryGetValue(key, out var val) == true ? val.GetInt32() : 0;
        }
        catch { return 0; }
    }
}
