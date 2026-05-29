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
