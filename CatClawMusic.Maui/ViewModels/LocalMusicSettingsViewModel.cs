using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 音乐文件夹项展示模型：表示已添加的本地音乐文件夹（含 SAF URI 与显示名）。
/// </summary>
public partial class MusicFolderItem : ObservableObject
{
    /// <summary>SAF URI（Android 文件夹选择器返回的标识）</summary>
    [ObservableProperty]
    private string _uri = "";

    /// <summary>展示用名称</summary>
    [ObservableProperty]
    private string _displayName = "";

    /// <summary>文件系统路径（自定义文件夹时使用）</summary>
    [ObservableProperty]
    private string _path = "";
}

/// <summary>
/// 本地音乐设置页 ViewModel：管理音乐文件夹列表、扫描选项（FFmpeg / MediaStore / SAF）、
/// 扫描进度与统计，以及自定义文件夹的增删等本地音乐相关配置。
/// </summary>
public partial class LocalMusicSettingsViewModel : ObservableObject
{
    private readonly LocalScanService _scanService;
    private readonly IPermissionService _permissionService;

    /// <summary>已添加的音乐文件夹集合</summary>
    [ObservableProperty]
    private ObservableCollection<MusicFolderItem> _musicFolders = new();

    /// <summary>扫描状态文本</summary>
    [ObservableProperty]
    private string _scanStatus = "未扫描";

    /// <summary>扫描进度（0.0 - 1.0）</summary>
    [ObservableProperty]
    private double _scanProgress = 0;

    /// <summary>是否正在扫描</summary>
    [ObservableProperty]
    private bool _isScanning = false;

    /// <summary>是否启用 FFmpeg 解码</summary>
    [ObservableProperty]
    private bool _isFfmpegEnabled = true;

    /// <summary>是否使用 Android MediaStore 进行扫描</summary>
    [ObservableProperty]
    private bool _useMediaStore = false;

    /// <summary>是否使用 SAF（Storage Access Framework）扫描</summary>
    [ObservableProperty]
    private bool _useSafScan = false;

    /// <summary>本次扫描发现的歌曲总数</summary>
    [ObservableProperty]
    private int _totalSongsFound = 0;

    /// <summary>本次扫描已导入的歌曲数</summary>
    [ObservableProperty]
    private int _totalSongsImported = 0;

    /// <summary>文件夹汇总文本（用于展示已添加文件夹数量）</summary>
    [ObservableProperty]
    private string _folderSummaryText = "未选择文件夹";

    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// 初始化 <see cref="LocalMusicSettingsViewModel"/> 实例，加载设置与已保存的文件夹。
    /// </summary>
    /// <param name="scanService">本地扫描服务</param>
    /// <param name="permissionService">权限服务</param>
    public LocalMusicSettingsViewModel(LocalScanService scanService, IPermissionService permissionService)
    {
        _scanService = scanService;
        _permissionService = permissionService;
        LoadSettings();
        _ = LoadSavedFoldersAsync();
    }

    private void LoadSettings()
    {
        IsFfmpegEnabled = Preferences.Get("ffmpeg_enabled", true);
        UseMediaStore = Preferences.Get("use_media_store", false);
        UseSafScan = Preferences.Get("use_saf_scan", false);
    }

    private void SaveSettings()
    {
        Preferences.Set("ffmpeg_enabled", IsFfmpegEnabled);
        Preferences.Set("use_media_store", UseMediaStore);
        Preferences.Set("use_saf_scan", UseSafScan);
    }

    partial void OnUseMediaStoreChanged(bool value)
    {
        Preferences.Set("use_media_store", value);
        if (value)
        {
            _ = RequestAudioPermissionAsync();
        }
    }

    partial void OnUseSafScanChanged(bool value)
    {
        Preferences.Set("use_saf_scan", value);
    }

    private async Task RequestAudioPermissionAsync()
    {
        try
        {
            var hasPermission = await _permissionService.RequestAudioPermissionAsync();
            if (!hasPermission)
            {
                UseMediaStore = false;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (Application.Current?.Windows[0]?.Page is Page page)
                        await page.DisplayAlertAsync("提示", "需要「音乐和音频」权限才能使用Android媒体库扫描功能", "确定");
                });
            }
        }
        catch { }
    }

    /// <summary>选择文件夹：调用 Android SAF 文件夹选择器添加音乐目录</summary>
    [RelayCommand]
    public async Task SelectFolderAsync()
    {
#if ANDROID
        try
        {
            var uri = await Platforms.Android.FolderPicker.PickFolderAsync();
            if (!string.IsNullOrEmpty(uri))
            {
                await LoadSavedFoldersAsync();
                ScanStatus = "已添加文件夹（可开始扫描）";
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

    /// <summary>移除指定的音乐文件夹</summary>
    /// <param name="folder">要移除的文件夹项，为空则忽略</param>
    [RelayCommand]
    public void RemoveFolder(MusicFolderItem? folder)
    {
        if (folder == null) return;
#if ANDROID
        try
        {
            if (!string.IsNullOrEmpty(folder.Uri))
            {
                Platforms.Android.FolderPicker.RemoveSavedFolder(folder.Uri);
            }
            else if (!string.IsNullOrEmpty(folder.Path) && folder.Path != "(SAF文件夹)")
            {
                RemoveCustomFolder(folder.Path);
            }
            _ = LoadSavedFoldersAsync();
            ScanStatus = "已删除文件夹";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalMusic] RemoveFolder error: {ex.Message}");
        }
#endif
    }

    /// <summary>扫描音乐：根据所选扫描方式扫描文件夹并导入歌曲，实时反馈进度</summary>
    [RelayCommand]
    public async Task ScanMusicAsync()
    {
        if (IsScanning) return;

        SaveSettings();

        if (UseMediaStore)
        {
            var hasPermission = await _permissionService.RequestAudioPermissionAsync();
            if (!hasPermission)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (Application.Current?.Windows[0]?.Page is Page page)
                        await page.DisplayAlertAsync("提示", "需要「音乐和音频」权限才能扫描，请先授予权限", "确定");
                });
                return;
            }
        }

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

            var imported = await _scanService.ScanAsync(progress, _scanCts.Token, UseMediaStore, UseSafScan);

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

    /// <summary>取消正在进行的扫描</summary>
    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
    }

    /// <summary>加载已保存的文件夹：合并 SAF 文件夹与自定义文件夹并刷新汇总文本</summary>
    public async Task LoadSavedFoldersAsync()
    {
        try
        {
#if ANDROID
            var savedUris = Platforms.Android.FolderPicker.GetSavedFolderUris();
            var customFolders = GetCustomFolders();
            MusicFolders.Clear();

            foreach (var uri in savedUris)
            {
                MusicFolders.Add(new MusicFolderItem
                {
                    Uri = uri,
                    DisplayName = ExtractDisplayName(uri),
                    Path = "(SAF文件夹)"
                });
            }

            foreach (var folder in customFolders)
            {
                MusicFolders.Add(new MusicFolderItem
                {
                    Uri = "",
                    DisplayName = Path.GetFileName(folder),
                    Path = folder
                });
            }

            var totalCount = savedUris.Count + customFolders.Count;
            if (totalCount > 0)
            {
                FolderSummaryText = $"已添加 {totalCount} 个文件夹";
                if (string.IsNullOrEmpty(ScanStatus) || ScanStatus == "未扫描")
                    ScanStatus = "就绪，点击「扫描音乐」开始";
            }
            else
            {
                FolderSummaryText = "未选择文件夹";
            }
#else
            await Task.CompletedTask;
#endif
        }
        catch { }
        await Task.CompletedTask;
    }

    private static List<string> GetCustomFolders()
    {
        try
        {
            var json = Preferences.Get("custom_music_folders", "");
            if (string.IsNullOrEmpty(json)) return new List<string>();
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    /// <summary>添加自定义文件夹路径到偏好（去重）</summary>
    /// <param name="path">文件夹路径</param>
    public static void AddCustomFolder(string path)
    {
        try
        {
            var folders = GetCustomFoldersStatic();
            if (!folders.Contains(path))
            {
                folders.Add(path);
                Preferences.Set("custom_music_folders", System.Text.Json.JsonSerializer.Serialize(folders));
            }
        }
        catch { }
    }

    /// <summary>从偏好中移除指定的自定义文件夹路径</summary>
    /// <param name="path">文件夹路径</param>
    public static void RemoveCustomFolder(string path)
    {
        try
        {
            var folders = GetCustomFoldersStatic();
            folders.Remove(path);
            Preferences.Set("custom_music_folders", System.Text.Json.JsonSerializer.Serialize(folders));
        }
        catch { }
    }

    private static List<string> GetCustomFoldersStatic()
    {
        try
        {
            var json = Preferences.Get("custom_music_folders", "");
            if (string.IsNullOrEmpty(json)) return new List<string>();
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch { return new List<string>(); }
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

    partial void OnIsFfmpegEnabledChanged(bool value)
    {
        Preferences.Set("ffmpeg_enabled", value);
    }
}
