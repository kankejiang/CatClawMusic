using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class LocalMusicSettingsViewModel : ObservableObject
{
    private readonly LocalScanService _scanService;

    [ObservableProperty]
    private string _musicFolderPath = "";

    [ObservableProperty]
    private string _scanStatus = "未扫描";

    [ObservableProperty]
    private double _scanProgress = 0;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private bool _isMp3Enabled = true;

    [ObservableProperty]
    private bool _isFlacEnabled = true;

    [ObservableProperty]
    private bool _isWavEnabled = true;

    [ObservableProperty]
    private int _totalSongsFound = 0;

    [ObservableProperty]
    private int _totalSongsImported = 0;

    private CancellationTokenSource? _scanCts;

    public LocalMusicSettingsViewModel(LocalScanService scanService)
    {
        _scanService = scanService;
        LoadSavedFolderAsync();
    }

    /// <summary>使用 SAF 选择音乐文件夹</summary>
    [RelayCommand]
    public async Task SelectFolderAsync()
    {
#if ANDROID
        try
        {
            var uri = await Platforms.Android.FolderPicker.PickFolderAsync();
            if (!string.IsNullOrEmpty(uri))
            {
                var displayName = ExtractDisplayName(uri);
                MusicFolderPath = displayName;
                var savedUris = Preferences.Get("music_folder_uris", "");
                if (!savedUris.Contains(uri))
                {
                    savedUris = string.IsNullOrEmpty(savedUris) ? uri : savedUris + "|" + uri;
                    Preferences.Set("music_folder_uris", savedUris);
                }
                ScanStatus = $"已添加文件夹: {displayName}（可开始扫描）";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalMusic] SelectFolder error: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>触发真实音乐扫描</summary>
    [RelayCommand]
    public async Task ScanMusicAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanProgress = 0;
        TotalSongsFound = 0;
        TotalSongsImported = 0;
        _scanCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(int done, int total, string status)>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScanProgress = p.total > 0 ? p.done / (double)p.total : 0;
                    ScanStatus = $"{p.status}";
                    if (p.done > 0) TotalSongsFound = p.done;
                    if (p.total > 0 && p.done >= p.total) TotalSongsImported = p.done;
                });
            });

            var imported = await _scanService.ScanAsync(progress, _scanCts.Token);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScanProgress = 1.0;
                TotalSongsImported = imported;
                ScanStatus = imported > 0
                    ? $"扫描完成！共导入 {imported} 首歌曲"
                    : "扫描完成，未发现新歌曲";
            });
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "扫描已取消";
        }
        catch (Exception ex)
        {
            ScanStatus = $"扫描失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[LocalMusic] Scan failed: {ex}");
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>取消扫描</summary>
    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
    }

    private async void LoadSavedFolderAsync()
    {
        try
        {
#if ANDROID
            var savedUris = Platforms.Android.FolderPicker.GetSavedFolderUris();
            if (savedUris.Count > 0)
            {
                MusicFolderPath = $"已添加 {savedUris.Count} 个文件夹";
                ScanStatus = "就绪，点击「扫描音乐」开始";
            }
#else
            var savedUris = Preferences.Get("music_folder_uris", "");
            if (!string.IsNullOrEmpty(savedUris))
            {
                var uris = savedUris.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (uris.Length > 0)
                {
                    MusicFolderPath = $"已添加 {uris.Length} 个文件夹";
                    ScanStatus = "就绪";
                }
            }
#endif
        }
        catch { }
        await Task.CompletedTask;
    }

#if ANDROID
    private static string ExtractDisplayName(string uri)
    {
        try
        {
            var lastColon = uri.LastIndexOf(':');
            var name = lastColon >= 0 ? uri[(lastColon + 1)..] : uri;
            var lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0) name = name[(lastSlash + 1)..];
            return Uri.UnescapeDataString(name);
        }
        catch { return uri; }
    }
#endif
}
