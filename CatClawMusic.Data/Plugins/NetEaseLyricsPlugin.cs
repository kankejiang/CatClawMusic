using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data.Plugins;

/// <summary>
/// 网易云音乐歌词插件 — 从网易音乐搜索并获取歌词
/// </summary>
public class NetEaseLyricsPlugin : ILyricsProviderPlugin
{
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private ILyricsService? _lyricsService;
    private bool _isAvailable;

    /// <inheritdoc />
    public string Name => "NetEaseLyrics";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string Author => "CatClawMusic Team";

    /// <inheritdoc />
    public string Description => "从网易音乐搜索并获取歌词，支持歌名+艺术家匹配";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// 创建网易歌词插件
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供者（用于延迟解析 ILyricsService，避免循环依赖）</param>
    public NetEaseLyricsPlugin(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://music.163.com");
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            // 测试网络连接：尝试访问网易搜索 API
            var testUrl = "https://music.163.com/api/search/get/web?s=test&type=1&limit=1";
            var response = await _httpClient.GetAsync(testUrl);
            _isAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            _isAvailable = false;
        }
    }

    /// <inheritdoc />
    public Task ShutdownAsync()
    {
        _isAvailable = false;
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        if (!_isAvailable) return null;

        try
        {
            // 1. 搜索歌曲
            var keyword = $"{song.Artist} {song.Title}".Trim();
            if (string.IsNullOrWhiteSpace(keyword)) return null;

            var searchUrl = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(keyword)}&type=1&limit=5";
            var searchResponse = await _httpClient.GetStringAsync(searchUrl);
            var searchJson = JsonDocument.Parse(searchResponse);

            // 检查搜索结果
            var root = searchJson.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() != 200) return null;
            if (!root.TryGetProperty("result", out var resultEl)) return null;
            if (!resultEl.TryGetProperty("songs", out var songsEl)) return null;

            // 取第一个歌曲 ID
            long songId = 0;
            foreach (var songItem in songsEl.EnumerateArray())
            {
                if (songItem.TryGetProperty("id", out var idEl))
                {
                    songId = idEl.GetInt64();
                    break;
                }
            }

            if (songId == 0) return null;

            // 2. 获取歌词
            var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1";
            var lyricResponse = await _httpClient.GetStringAsync(lyricUrl);
            var lyricJson = JsonDocument.Parse(lyricResponse);

            var lyricRoot = lyricJson.RootElement;
            if (lyricRoot.TryGetProperty("code", out var lrcCodeEl) && lrcCodeEl.GetInt32() != 200) return null;

            // 解析 lrcContent
            string? lrcContent = null;
            if (lyricRoot.TryGetProperty("lrc", out var lrcEl) && lrcEl.TryGetProperty("lyric", out var lyricTextEl))
            {
                lrcContent = lyricTextEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(lrcContent)) return null;

            // 3. 使用 LyricsService.ParseLrc 解析 LRC 文本
            _lyricsService ??= _serviceProvider.GetService(typeof(ILyricsService)) as ILyricsService;
            if (_lyricsService == null) return null;
            return _lyricsService.ParseLrc(lrcContent);
        }
        catch
        {
            // 搜索失败或无结果时返回 null，不抛异常
            return null;
        }
    }
}
