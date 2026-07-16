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

/// <summary>
/// 创建歌单工具，用于在音乐库中创建一个新的播放列表。
/// </summary>
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

/// <summary>
/// 添加歌曲到歌单工具，将指定歌曲加入目标播放列表。
/// </summary>
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

/// <summary>
/// 从歌单移除歌曲工具，将指定歌曲从目标播放列表中删除。
/// </summary>
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

/// <summary>
/// 列出所有歌单工具，返回用户全部播放列表概要信息。
/// </summary>
public class ListPlaylistsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "list_playlists";
    /// <summary>工具描述</summary>
    public string Description => "获取用户所有播放列表（歌单）";

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

/// <summary>
/// 获取歌单歌曲列表工具，返回指定播放列表内的全部歌曲。
/// </summary>
public class GetPlaylistSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_playlist_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取指定歌单中的歌曲列表";

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

/// <summary>
/// 删除歌单工具，删除指定的播放列表。
/// </summary>
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

/// <summary>
/// 播放歌曲工具，根据歌曲 ID 查找歌曲并加入播放队列后立即播放。
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

/// <summary>
/// 联网搜索工具，通过 DuckDuckGo 在互联网上搜索信息。
/// 优先使用 DuckDuckGo HTML 接口（POST），失败时回退到 Lite 版本。
/// </summary>
public class WebSearchTool : IAgentTool
{
    /// <summary>HTTP 客户端，用于发起搜索请求</summary>
    private readonly HttpClient _httpClient;
    /// <summary>工具名称</summary>
    public string Name => "web_search";
    /// <summary>工具描述</summary>
    public string Description => "在互联网上搜索信息，可以搜索新闻、知识、音乐资讯等内容。当用户询问实时信息、最新资讯或你不确定的知识时使用此工具。";

    /// <summary>
    /// 构造 WebSearchTool 实例，初始化 HttpClient 并设置完整的浏览器请求头。
    /// </summary>
    public WebSearchTool()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
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
                    ["query"] = new() { Type = "string", Description = "搜索关键词" }
                },
                Required = new List<string> { "query" }
            }
        }
    };

    /// <summary>
    /// 执行联网搜索操作
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串，包含 query 字段</param>
    /// <returns>JSON 序列化结果，包含 success、query、results、message 字段</returns>
    public async Task<string> ExecuteAsync(string arguments)
    {
        var query = ArgHelper.ExtractStringArgFallback(arguments, "query");

        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = "请提供搜索关键词" });

        try
        {
            // 优先使用 DuckDuckGo HTML 接口（POST 方式，更稳定）
            var results = await SearchDuckDuckGoHtmlAsync(query);

            // 若 HTML 接口无结果，回退到 Lite 版本
            if (results.Count == 0)
            {
                results = await SearchDuckDuckGoLiteAsync(query);
            }

            if (results.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    query = query,
                    results = results,
                    message = $"搜索完成，找到 {results.Count} 条相关结果"
                });
            }

            return JsonSerializer.Serialize(new
            {
                success = false,
                query = query,
                results = Array.Empty<object>(),
                message = $"已搜索「{query}」但未找到相关结果，建议换个关键词试试"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"网络搜索失败: {ex.Message}" });
        }
    }

    /// <summary>
    /// 通过 DuckDuckGo HTML 接口搜索（POST 方式）
    /// </summary>
    private async Task<List<object>> SearchDuckDuckGoHtmlAsync(string query)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = query,
                ["b"] = ""
            });
            var response = await _httpClient.PostAsync("https://html.duckduckgo.com/html/", content);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            return ParseDuckDuckGoHtmlResults(html);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] DuckDuckGo HTML 搜索失败: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>
    /// 通过 DuckDuckGo Lite 接口搜索（GET 方式，更简单的 HTML 结构）
    /// </summary>
    private async Task<List<object>> SearchDuckDuckGoLiteAsync(string query)
    {
        try
        {
            var searchUrl = $"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(searchUrl);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            return ParseDuckDuckGoLiteResults(html);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] DuckDuckGo Lite 搜索失败: {ex.Message}");
            return new List<object>();
        }
    }

    /// <summary>
    /// 解析 DuckDuckGo HTML 搜索结果页（class="result__a" / class="result__snippet"）
    /// </summary>
    private List<object> ParseDuckDuckGoHtmlResults(string html)
    {
        var results = new List<object>();
        try
        {
            // 匹配所有结果链接（result__a class）
            var linkPattern = @"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>";
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(html, linkPattern);

            // 匹配所有摘要（result__snippet class）
            var snippetPattern = @"<a[^>]*class=""result__snippet""[^>]*>([\s\S]*?)</a>";
            var snippetMatches = System.Text.RegularExpressions.Regex.Matches(html, snippetPattern);

            var count = Math.Min(linkMatches.Count, 5);
            for (int i = 0; i < count; i++)
            {
                var url = linkMatches[i].Groups[1].Value;
                var title = CleanHtmlText(linkMatches[i].Groups[2].Value);
                var snippet = i < snippetMatches.Count ? CleanHtmlText(snippetMatches[i].Groups[1].Value) : "";

                // DuckDuckGo 的 URL 可能是重定向链接（//duckduckgo.com/l/?uddg=...），需要解码
                url = DecodeDuckDuckGoUrl(url);

                if (!string.IsNullOrEmpty(title))
                {
                    results.Add(new { title, url, snippet });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] 解析 HTML 结果失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// 解析 DuckDuckGo Lite 搜索结果页（表格结构，class="result-link"）
    /// </summary>
    private List<object> ParseDuckDuckGoLiteResults(string html)
    {
        var results = new List<object>();
        try
        {
            // Lite 版本使用表格布局，链接在 class="result-link" 中
            var linkPattern = @"<a[^>]*class=""result-link""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>";
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(html, linkPattern);

            // 摘要在链接所在行的下一个 td 中
            var tdPattern = @"<td[^>]*class=""result-snippet""[^>]*>([\s\S]*?)</td>";
            var snippetMatches = System.Text.RegularExpressions.Regex.Matches(html, tdPattern);

            var count = Math.Min(linkMatches.Count, 5);
            for (int i = 0; i < count; i++)
            {
                var url = linkMatches[i].Groups[1].Value;
                var title = CleanHtmlText(linkMatches[i].Groups[2].Value);
                var snippet = i < snippetMatches.Count ? CleanHtmlText(snippetMatches[i].Groups[1].Value) : "";

                url = DecodeDuckDuckGoUrl(url);

                if (!string.IsNullOrEmpty(title))
                {
                    results.Add(new { title, url, snippet });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebSearch] 解析 Lite 结果失败: {ex.Message}");
        }
        return results;
    }

    /// <summary>清理 HTML 文本：去除标签、解码实体、压缩空白</summary>
    private static string CleanHtmlText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        // 去除所有 HTML 标签
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", "");
        // 解码常见 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);
        // 压缩空白
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    /// <summary>解码 DuckDuckGo 重定向 URL（//duckduckgo.com/l/?uddg=ENCODED_URL）</summary>
    private static string DecodeDuckDuckGoUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.StartsWith("//")) url = "https:" + url;
        // 检查是否是 DuckDuckGo 重定向链接
        var uddgIdx = url.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
        if (uddgIdx > 0)
        {
            var encoded = url[(uddgIdx + 5)..];
            var ampIdx = encoded.IndexOf('&');
            if (ampIdx >= 0) encoded = encoded[..ampIdx];
            try
            {
                var decoded = Uri.UnescapeDataString(encoded);
                if (Uri.TryCreate(decoded, UriKind.Absolute, out _))
                    return decoded;
            }
            catch { }
        }
        return url;
    }
}

/// <summary>
/// 播放控制工具，支持暂停、恢复、上一首、下一首、停止、调节音量与跳转进度等操作。
/// </summary>
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

/// <summary>
/// 获取当前播放歌曲工具，返回正在播放歌曲的详细信息及播放进度。
/// </summary>
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

/// <summary>
/// 获取播放队列工具，返回当前播放模式、队列总歌曲数与即将播放的歌曲。
/// </summary>
public class GetPlayQueueTool : IAgentTool
{
    /// <summary>播放队列</summary>
    private readonly PlayQueue _playQueue;
    /// <summary>工具名称</summary>
    public string Name => "get_play_queue";
    /// <summary>工具描述</summary>
    public string Description => "获取当前播放队列信息，包括播放模式、队列中的歌曲和即将播放的歌曲";

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

/// <summary>
/// 切换收藏状态工具，对指定歌曲进行收藏或取消收藏操作。
/// </summary>
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

/// <summary>
/// 获取收藏歌曲列表工具，返回用户已收藏的歌曲。
/// </summary>
public class GetFavoriteSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_favorite_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取收藏的歌曲列表";

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

/// <summary>
/// 获取最近播放歌曲工具，返回用户最近播放过的歌曲列表。
/// </summary>
public class GetRecentSongsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_recent_songs";
    /// <summary>工具描述</summary>
    public string Description => "获取最近播放的歌曲列表";

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

/// <summary>
/// 获取播放统计工具，返回播放次数最多的歌曲排行榜数据。
/// </summary>
public class GetListeningStatsTool : IAgentTool
{
    /// <summary>音乐库服务</summary>
    private readonly IMusicLibraryService _musicLibrary;
    /// <summary>工具名称</summary>
    public string Name => "get_listening_stats";
    /// <summary>工具描述</summary>
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

/// <summary>
/// 添加到播放队列工具，将歌曲添加为下一首播放或队列末尾。
/// </summary>
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

/// <summary>
/// 清空播放队列工具，停止播放并清空当前队列。
/// </summary>
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

/// <summary>
/// 工具参数解析辅助类，从 JSON 参数字符串中提取字符串或整数参数。
/// </summary>
internal static class ArgHelper
{
    /// <summary>
    /// 从 JSON 参数字符串中提取字符串参数
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串</param>
    /// <param name="key">参数键名</param>
    /// <returns>参数值；解析失败或不存在时返回 null</returns>
    internal static string? ExtractStringArgFallback(string arguments, string key)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            return args?.TryGetValue(key, out var val) == true ? val.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 从 JSON 参数字符串中提取整数参数
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串</param>
    /// <param name="key">参数键名</param>
    /// <returns>参数值；解析失败或不存在时返回 0</returns>
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
