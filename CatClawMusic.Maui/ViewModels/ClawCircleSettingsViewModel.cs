using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 猫爪圈设置页 ViewModel：管理开关、设备名、共享开关、扫描邻近设备、浏览与拉取对端歌曲。
/// </summary>
public partial class ClawCircleSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IClawCircleService _service;
    private readonly MusicDatabase _db;
    private bool _subscribed;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private bool _shareLibrary = true;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _trackerUrl = "";
    [ObservableProperty] private string _trackerToken = "";

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _localEndpoint = "";
    [ObservableProperty] private string _statusText = "未开启";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _downloadMessage = "";

    [ObservableProperty] private ClawCirclePeer? _selectedPeer;
    [ObservableProperty] private bool _isLoadingPeerSongs;

    public ObservableCollection<ClawCirclePeer> Peers { get; } = new();
    public ObservableCollection<ClawCircleSongInfo> PeerSongs { get; } = new();

    [ObservableProperty] private bool _hasPeers;
    [ObservableProperty] private bool _hasPeerSongs;

    /// <summary>「浏览」按钮文案（避免在 DataTemplate 内使用索引器绑定）。</summary>
    public string BrowseLabel => LocalizationService.Instance["ClawCircleBrowse"];

    /// <summary>「拉取」按钮文案（避免在 DataTemplate 内使用索引器绑定）。</summary>
    public string PullLabel => LocalizationService.Instance["ClawCircleDownload"];

    public ClawCircleSettingsViewModel(IClawCircleService service, MusicDatabase db)
    {
        _service = service;
        _db = db;
        _cts = new CancellationTokenSource();

        var settings = ClawCircleSettingsStore.Load();
#pragma warning disable MVVMTK0034
        _isEnabled = settings.Enabled;
        _deviceName = string.IsNullOrWhiteSpace(settings.DeviceName)
            ? $"猫爪-{Environment.MachineName}"
            : settings.DeviceName;
        _shareLibrary = settings.ShareLibrary;
        _autoStart = settings.AutoStart;
        _trackerUrl = settings.TrackerUrl;
        _trackerToken = settings.TrackerToken;
#pragma warning restore MVVMTK0034
    }

    /// <summary>页面出现时调用：若开启自动启动且未运行，则自动开启。</summary>
    public async Task OnAppearingAsync()
    {
        if (IsEnabled && !_service.IsRunning)
        {
            await StartAsync();
        }
        else if (_service.IsRunning)
        {
            SyncRunningState();
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        SaveSettings();
        if (value)
            _ = StartAsync();
        else
            _ = StopAsync();
    }

    partial void OnDeviceNameChanged(string value)
    {
        SaveSettings();
        // 运行中改名需重启以生效
        if (_service.IsRunning)
            _ = RestartAsync();
    }

    partial void OnShareLibraryChanged(bool value)
    {
        SaveSettings();
        if (_service.IsRunning)
            _ = RestartAsync();
    }

    partial void OnAutoStartChanged(bool value) => SaveSettings();

    partial void OnTrackerUrlChanged(string value) => SaveSettings();
    partial void OnTrackerTokenChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        ClawCircleSettingsStore.Save(new ClawCircleSettings
        {
            Enabled = IsEnabled,
            DeviceName = DeviceName,
            ShareLibrary = ShareLibrary,
            AutoStart = AutoStart,
            TrackerUrl = TrackerUrl,
            TrackerToken = TrackerToken
        });
    }

    private async Task StartAsync()
    {
        try
        {
            EnsureSubscribed();
            await _service.StartAsync(DeviceName, ShareLibrary);
            SyncRunningState();
            _ = _service.RefreshPeersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"启动失败：{ex.Message}";
            IsRunning = false;
        }
    }

    private async Task StopAsync()
    {
        await _service.StopAsync();
        Unsubscribe();
        SyncRunningState();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Peers.Clear();
            PeerSongs.Clear();
            SelectedPeer = null;
            HasPeers = false;
            HasPeerSongs = false;
        });
    }

    private async Task RestartAsync()
    {
        await _service.StopAsync();
        await StartAsync();
        // 重启后刷新本机信息
        SyncRunningState();
    }

    private void SyncRunningState()
    {
        IsRunning = _service.IsRunning;
        LocalEndpoint = _service.IsRunning && !string.IsNullOrEmpty(_service.LocalAddress)
            ? $"{_service.LocalAddress}:{_service.Port}"
            : "";
        StatusText = _service.IsRunning
            ? (ShareLibrary ? "运行中 · 已共享曲库" : "运行中 · 仅发现")
            : "未开启";
        if (_service.IsRunning)
            RefreshPeersList();
    }

    private void EnsureSubscribed()
    {
        if (_subscribed) return;
        _service.PeersChanged += OnPeersChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _service.PeersChanged -= OnPeersChanged;
        _subscribed = false;
    }

    private void OnPeersChanged(object? sender, EventArgs e) => RefreshPeersList();

    private void RefreshPeersList()
    {
        var peers = _service.GetPeers();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Peers.Clear();
            foreach (var p in peers.OrderBy(x => x.DeviceName))
                Peers.Add(p);
            HasPeers = Peers.Count > 0;
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            await _service.RefreshPeersAsync();
            // 给对端一点回包时间
            await Task.Delay(1500, _cts?.Token ?? CancellationToken.None);
            RefreshPeersList();
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task BrowsePeerAsync(ClawCirclePeer peer)
    {
        if (peer == null) return;
        SelectedPeer = peer;
        PeerSongs.Clear();
        IsLoadingPeerSongs = true;
        try
        {
            var songs = await _service.GetPeerSongsAsync(peer, _cts?.Token ?? CancellationToken.None);
            if (songs != null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    foreach (var s in songs)
                        PeerSongs.Add(s);
                    HasPeerSongs = PeerSongs.Count > 0;
                });
            }
        }
        catch (Exception ex)
        {
            DownloadMessage = $"获取歌单失败：{ex.Message}";
        }
        finally
        {
            IsLoadingPeerSongs = false;
        }
    }

    [RelayCommand]
    private async Task DownloadSongAsync(ClawCircleSongInfo song)
    {
        if (song == null || SelectedPeer == null) return;
        DownloadMessage = $"正在从 {SelectedPeer.DeviceName} 拉取《{song.Title}》…";
        try
        {
            var result = await _service.GetPeerSongStreamAsync(SelectedPeer, song.Id, _cts?.Token ?? CancellationToken.None);
            if (result?.Stream == null)
            {
                DownloadMessage = "拉取失败：无法获取音频流";
                return;
            }

            var dir = Path.Combine(FileSystem.AppDataDirectory, "clawcircle", "downloads");
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp3";
            var safeTitle = string.Concat(song.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if (safeTitle.Length > 40) safeTitle = safeTitle.Substring(0, 40);
            var dest = Path.Combine(dir, $"{song.Id}_{safeTitle}{ext}");

            long written;
            await using (result.Stream)
            {
                await using var fs = File.Create(dest);
                await result.Stream.CopyToAsync(fs, _cts?.Token ?? CancellationToken.None);
                written = fs.Length;
            }

            // 加入本地音乐库
            var newSong = new Song
            {
                Title = song.Title,
                Artist = song.Artist,
                Album = song.Album,
                Duration = song.DurationMs,
                FilePath = dest,
                FileSize = written,
                Source = SongSource.Local,
                DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                DateModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await _db.InsertSongsBatchAsync(new List<Song> { newSong });

            DownloadMessage = $"已保存《{song.Title}》并加入音乐库";
        }
        catch (Exception ex)
        {
            DownloadMessage = $"下载失败：{ex.Message}";
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        Unsubscribe();
    }
}
