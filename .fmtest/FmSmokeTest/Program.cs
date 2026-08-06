using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Plugins.Netease;

class Program
{
    static async Task Main()
    {
        var fake = new FakeNetease();
        var queue = new PlayQueue();
        var audio = new FakeAudioPlayer();
        var vm = new NeteaseOnlineMusicViewModel(fake, queue, audio);

        int failures = 0;
        void Check(string name, bool cond)
        {
            Console.WriteLine($"[{(cond ? "PASS" : "FAIL")}] {name}");
            if (!cond) failures++;
        }

        // 1) 私人漫游应返回多首（验证"只有一首"的 bug 已修复）
        await vm.LoadPrivateFmAsync();
        Check("私人漫游加载出多首歌曲(>1)", vm.Songs.Count > 1);
        Check("私人漫游加载出整批(>=8)", vm.Songs.Count >= 8);

        // 2) 点击播放 -> 整批 FM 入队（而非只塞 1 首）
        var first = vm.Songs[0];
        await vm.PlaySongAsync(first);
        Check("点击后整批 FM 入播放队列", queue.GetSongs().Count == vm.Songs.Count);
        Check("播放队列非空", queue.GetSongs().Count > 0);

        // 3) 模拟"播完自动续播"：每次播放完成触发缓冲，宿主前进到下一首
        int startCount = queue.GetSongs().Count;
        const int steps = 30;
        for (int i = 0; i < steps; i++)
        {
            // 等价于宿主 AppViewModels.OnPlaybackCompleted -> VM.OnAudioPlaybackCompleted -> EnsureFmBufferAsync
            await InvokeEnsureFmBufferAsync(vm);
            var next = queue.Next();
            if (next == null)
            {
                Check($"第 {i} 步队列未枯竭（无限电台）", false);
                break;
            }
            queue.SelectSong(next.Id);
        }
        Check("连续播放 30 首后队列持续增长（无限续播）", queue.GetSongs().Count > startCount);
        Check("队列无重复歌曲（去重生效）", HasNoDuplicateRemoteId(queue));

        Console.WriteLine($"\n初始批次={startCount}，30 步后队列={queue.GetSongs().Count}");
        Console.WriteLine(failures == 0 ? "\n=== ALL PASS ===" : $"\n=== {failures} FAILED ===");
        Environment.Exit(failures == 0 ? 0 : 1);
    }

    static Task InvokeEnsureFmBufferAsync(object vm)
    {
        var m = vm.GetType().GetMethod("EnsureFmBufferAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("EnsureFmBufferAsync not found");
        return (Task)m.Invoke(vm, null)!;
    }

    static bool HasNoDuplicateRemoteId(PlayQueue q)
    {
        var seen = new HashSet<string>();
        foreach (var s in q.GetSongs())
        {
            if (s.RemoteId == null) continue;
            if (!seen.Add(s.RemoteId)) return false;
        }
        return true;
    }
}

/// <summary>假网易云音源：GetPrivateFmAsync 每次返回一批带唯一 id 的新歌，模拟无限电台</summary>
class FakeNetease : IOnlineMusicPlugin
{
    private int _counter;
    public string PluginId => "fake";
    public string Name => "fake";
    public string Version => "0";
    public string Author => "test";
    public string Description => "fake";
    public List<string> Capabilities => new();
    public string PlatformName => "netease";

    public Task<List<OnlineSong>?> GetPrivateFmAsync(int num = 10)
    {
        var list = new List<OnlineSong>();
        for (int i = 0; i < num; i++)
        {
            _counter++;
            list.Add(new OnlineSong
            {
                Id = "fm-" + _counter,
                Platform = "netease",
                Title = "歌曲 " + _counter,
                Artist = "测试艺人",
                Album = "测试专辑",
                DurationMs = 180000,
            });
        }
        return Task.FromResult<List<OnlineSong>?>(list);
    }

    public Task<string?> GetPlayUrlAsync(OnlineSong song, int quality = 0)
        => Task.FromResult<string?>($"http://fake/{song.Id}");

    public Task<List<OnlineSong>?> SearchAsync(string keyword, int page = 1, int pageSize = 8)
        => Task.FromResult<List<OnlineSong>?>(new());
    public Task<List<OnlinePlaylist>> GetPlaylistsAsync(string? category = null)
        => Task.FromResult(new List<OnlinePlaylist>());
    public Task<List<OnlineSong>?> GetPlaylistSongsAsync(OnlinePlaylist playlist, int page = 1, int pageSize = 50)
        => Task.FromResult<List<OnlineSong>?>(new());
    public Task<List<OnlinePlaylist>> GetToplistsAsync()
        => Task.FromResult(new List<OnlinePlaylist>());
    public Task<List<OnlineSong>?> GetDailyRecommendAsync(int num = 20)
        => Task.FromResult<List<OnlineSong>?>(new());
    public Task<BrowserLoginInfo?> GetBrowserLoginInfoAsync()
        => Task.FromResult<BrowserLoginInfo?>(null);
    public Task SetLoginCookieAsync(string cookie) => Task.CompletedTask;
    public Task<bool> IsLoggedInAsync() => Task.FromResult(false);
    public Task<string?> GetAccountNameAsync() => Task.FromResult<string?>(null);
    public Task LogoutAsync() => Task.CompletedTask;
    public Task<(string? Lrc, string? TLrc)?> GetLyricsAsync(OnlineSong song)
        => Task.FromResult<(string? Lrc, string? TLrc)?>(null);
    public Task InitializeAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
}

/// <summary>假播放器：PlaybackCompleted 可手动触发，其余方法空实现</summary>
class FakeAudioPlayer : IAudioPlayerService
{
    public bool IsPlaying => false;
    public double CurrentPosition => 0;
    public double Duration => 0;
    public double Volume { get; set; }
    public string? CurrentSongFilePath => null;
    public event EventHandler<bool>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<double>? DurationChanged;
    public event EventHandler? PlaybackCompleted;
    public Task PlayAsync(string filePath) => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;
}
