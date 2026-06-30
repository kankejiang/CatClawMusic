using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class GeneralSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private List<string> _languageOptions = new() { "简体中文", "English" };

    [ObservableProperty]
    private int _selectedLanguageIndex = 0;

    [ObservableProperty]
    private string _cacheSize = "计算中...";

    [ObservableProperty]
    private bool _isClearingCache = false;

    public GeneralSettingsViewModel()
    {
        _ = RefreshCacheSizeAsync();
    }

    /// <summary>
    /// 清除应用缓存（音乐缓存目录）
    /// </summary>
    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        if (IsClearingCache) return;
        IsClearingCache = true;
        try
        {
            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "music_cache");
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
                Directory.CreateDirectory(cacheDir);
            }
            await RefreshCacheSizeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeneralVM] ClearCache failed: {ex}");
        }
        finally { IsClearingCache = false; }
    }

    /// <summary>
    /// 恢复默认设置
    /// </summary>
    [RelayCommand]
    public async Task ResetSettingsAsync()
    {
        try
        {
            Preferences.Clear();
            await RefreshCacheSizeAsync();
            SelectedLanguageIndex = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeneralVM] ResetSettings failed: {ex}");
        }
    }

    /// <summary>
    /// 刷新缓存大小显示
    /// </summary>
    public async Task RefreshCacheSizeAsync()
    {
        try
        {
            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "music_cache");
            long size = await Task.Run(() => GetDirectorySize(cacheDir));
            CacheSize = FormatSize(size);
        }
        catch
        {
            CacheSize = "不可用";
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch { }
        return size;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 MB";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
