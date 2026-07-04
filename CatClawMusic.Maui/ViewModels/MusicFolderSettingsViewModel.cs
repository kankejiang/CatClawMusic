using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 音乐文件夹设置页 ViewModel：管理已添加的文件夹列表、扫描选项（子目录扫描/自动扫描）
/// 与扫描进度，提供文件夹的增删与批量扫描能力。
/// </summary>
public partial class MusicFolderSettingsViewModel : ObservableObject
{
    private readonly List<string> _folderUris = new();
    private readonly LocalScanService? _scanService;

    /// <summary>音乐文件夹显示名集合</summary>
    [ObservableProperty]
    private ObservableCollection<string> _musicFolders = new();

    /// <summary>是否扫描子文件夹</summary>
    [ObservableProperty]
    private bool _scanSubfolders = true;

    /// <summary>是否在添加文件夹后自动扫描</summary>
    [ObservableProperty]
    private bool _autoScan = false;

    /// <summary>是否正在扫描</summary>
    [ObservableProperty]
    private bool _isScanning = false;

    /// <summary>扫描进度（0.0 - 1.0）</summary>
    [ObservableProperty]
    private double _scanProgress = 0;

    /// <summary>扫描状态文本</summary>
    [ObservableProperty]
    private string _scanStatus = "";

    /// <summary>本次扫描已导入的歌曲数</summary>
    [ObservableProperty]
    private int _totalSongsImported = 0;

    /// <summary>
    /// 初始化 <see cref="MusicFolderSettingsViewModel"/> 实例，并加载已保存的文件夹。
    /// </summary>
    /// <param name="scanService">本地扫描服务，可为空（设计时支持）</param>
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

    /// <summary>添加文件夹：调用 Android SAF 选择器，添加后若开启自动扫描则立即触发扫描</summary>
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

    /// <summary>移除指定的文件夹：从集合与持久化记录中同步删除</summary>
    /// <param name="folder">要移除的文件夹显示名，为空则忽略</param>
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
