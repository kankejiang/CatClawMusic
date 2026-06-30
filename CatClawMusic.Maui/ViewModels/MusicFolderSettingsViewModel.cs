using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class MusicFolderSettingsViewModel : ObservableObject
{
    private readonly List<string> _folderUris = new();
    private readonly LocalScanService? _scanService;

    [ObservableProperty]
    private ObservableCollection<string> _musicFolders = new();

    [ObservableProperty]
    private bool _scanSubfolders = true;

    [ObservableProperty]
    private bool _autoScan = false;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private double _scanProgress = 0;

    [ObservableProperty]
    private string _scanStatus = "";

    [ObservableProperty]
    private int _totalSongsImported = 0;

    public MusicFolderSettingsViewModel(LocalScanService? scanService = null)
    {
        _scanService = scanService;
        LoadSavedFolders();
    }

    /// <summary>扫描所有已添加的文件夹</summary>
    [RelayCommand]
    public async Task ScanAllFoldersAsync()
    {
        if (_scanService == null || IsScanning) return;

        IsScanning = true;
        ScanProgress = 0;
        ScanStatus = "正在扫描...";
        TotalSongsImported = 0;

        try
        {
            var progress = new Progress<(int done, int total, string status)>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScanProgress = p.total > 0 ? p.done / (double)p.total : 0;
                    ScanStatus = p.status;
                });
            });

            var imported = await _scanService.ScanAsync(progress);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScanProgress = 1.0;
                TotalSongsImported = imported;
                ScanStatus = imported > 0
                    ? $"扫描完成！共导入 {imported} 首歌曲"
                    : "未发现新歌曲";
            });
        }
        catch (Exception ex)
        {
            ScanStatus = $"扫描失败: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
#if ANDROID
        try
        {
            var uri = await Platforms.Android.FolderPicker.PickFolderAsync();
            if (!string.IsNullOrEmpty(uri) && !_folderUris.Contains(uri))
            {
                _folderUris.Add(uri);
                var displayName = ExtractDisplayName(uri);
                MusicFolders.Add(displayName);

                // 如果开启了自动扫描，立即触发
                if (AutoScan)
                    _ = ScanAllFoldersAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicFolder] AddFolder error: {ex.Message}");
        }
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    private void RemoveFolder(string? folder)
    {
        if (folder == null) return;

        var index = MusicFolders.IndexOf(folder);
        if (index < 0) return;

        MusicFolders.RemoveAt(index);
        if (index < _folderUris.Count)
        {
            var uri = _folderUris[index];
            _folderUris.RemoveAt(index);

#if ANDROID
            try { Platforms.Android.FolderPicker.RemoveSavedFolder(uri); }
            catch { }
#endif
        }
    }

    private void LoadSavedFolders()
    {
#if ANDROID
        try
        {
            var uris = Platforms.Android.FolderPicker.GetSavedFolderUris();
            foreach (var uri in uris)
            {
                _folderUris.Add(uri);
                MusicFolders.Add(ExtractDisplayName(uri));
            }
        }
        catch { }
#endif
    }

    private static string ExtractDisplayName(string uri)
    {
        try
        {
            if (uri.Contains(':'))
            {
                var lastColon = uri.LastIndexOf(':');
                var path = uri[(lastColon + 1)..];
                if (!string.IsNullOrEmpty(path)) return path;
            }
            if (uri.Contains('/'))
            {
                var lastSlash = uri.LastIndexOf('/');
                return uri[(lastSlash + 1)..];
            }
        }
        catch { }
        return uri;
    }
}
