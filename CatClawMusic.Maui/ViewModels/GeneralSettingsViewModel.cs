using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CatClawMusic.Maui.Services;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 通用设置页 ViewModel：管理语言选择、缓存大小展示与清理、恢复默认设置等通用配置项。
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    /// <summary>可选语言列表（各语言以自身名称显示，首项为"跟随系统"）</summary>
    [ObservableProperty]
    private List<string> _languageOptions = new()
    {
        "跟随系统",
        "简体中文", "繁體中文", "English", "日本語", "한국어",
        "Français", "Deutsch", "Español", "Русский", "Português", "Italiano",
        "العربية", "हिन्दी", "বাংলা", "தமிழ்", "తెలుగు", "मराठी", "ગુજરાતી", "ಕನ್ನಡ", "മലയാളം",
        "Bahasa Indonesia", "Bahasa Melayu", "ไทย", "Tiếng Việt",
        "Nederlands", "Polski", "Türkçe", "Українська",
        "Svenska", "Norsk bokmål", "Dansk", "Suomi",
        "Čeština", "Magyar", "Română", "Ελληνικά", "עברית",
        "Slovenčina", "Hrvatski", "Српски", "Български",
        "Filipino", "اردو", "فارسی", "Kiswahili",
        "Català", "Latviešu", "Lietuvių", "Eesti", "Slovenščina", "Íslenska"
    };

    /// <summary>当前选中的语言索引</summary>
    [ObservableProperty]
    private int _selectedLanguageIndex = 0;

    /// <summary>当前缓存大小展示文本</summary>
    [ObservableProperty]
    private string _cacheSize = "计算中...";

    /// <summary>是否正在清除缓存</summary>
    [ObservableProperty]
    private bool _isClearingCache = false;

    /// <summary>缓存大小上限（MB），绑定到设置页 Slider</summary>
    [ObservableProperty]
    private int _cacheSizeLimitMB;

    /// <summary>缓存大小上限展示文本</summary>
    [ObservableProperty]
    private string _cacheSizeLimitText = "";

    /// <summary>当前图片缓存大小展示文本</summary>
    [ObservableProperty]
    private string _imageCacheSize = "计算中…";

    /// <summary>图片缓存上限（MB），绑定到设置页 Slider</summary>
    [ObservableProperty]
    private double _imageCacheSizeLimitMB;

    /// <summary>图片缓存上限展示文本</summary>
    [ObservableProperty]
    private string _imageCacheLimitText = "";

    /// <summary>图片缓存有效期（天），绑定到设置页 Slider</summary>
    [ObservableProperty]
    private double _imageCacheAgeLimitDays;

    /// <summary>图片缓存有效期展示文本</summary>
    [ObservableProperty]
    private string _imageCacheAgeText = "";

    /// <summary>是否正在清除图片缓存</summary>
    [ObservableProperty]
    private bool _isClearingImageCache = false;

    /// <summary>
    /// 初始化 <see cref="GeneralSettingsViewModel"/> 实例，并立即刷新缓存大小。
    /// </summary>
    public GeneralSettingsViewModel()
    {
        // 打开设置页时，Picker 反映当前已保存的语言
        SelectedLanguageIndex = LocalizationService.GetSavedCultureIndex();
        CacheSizeLimitMB = Preferences.Default.Get("audio_cache_size_mb", AudioCacheService.DefaultCacheSizeMB);
        UpdateCacheSizeLimitText();

        ImageCacheSizeLimitMB = ImageCacheService.Instance.CacheSizeLimitMB;
        ImageCacheAgeLimitDays = ImageCacheService.Instance.CacheAgeDays;
        UpdateImageCacheLimitText();
        UpdateImageCacheAgeText();

        _ = RefreshCacheSizeAsync();
    }

    /// <summary>
    /// 语言选择变化时立即切换应用语言（持久化 + 应用文化 + 通知所有绑定刷新）。
    /// </summary>
    partial void OnSelectedLanguageIndexChanged(int value)
    {
        LocalizationService.Instance.SetLanguageByIndex(value);
    }

    /// <summary>
    /// 清除应用缓存（音乐缓存 + 图片缓存）。
    /// </summary>
    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        if (IsClearingCache) return;
        IsClearingCache = true;
        try
        {
            await AudioCacheService.Instance.ClearAllAsync();
            // 也清理旧的本地封面缓存目录
            var coverDir = Path.Combine(FileSystem.CacheDirectory, "covers");
            if (Directory.Exists(coverDir))
            {
                Directory.Delete(coverDir, true);
                Directory.CreateDirectory(coverDir);
            }
            await ImageCacheService.Instance.ClearAllAsync();
            await RefreshCacheSizeAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("GeneralSettingsViewModel", $"[GeneralVM] ClearCache failed: {ex}");
        }
        finally { IsClearingCache = false; }
    }

    /// <summary>
    /// 清除图片缓存（网络封面、艺术家/专辑封面）。
    /// </summary>
    [RelayCommand]
    public async Task ClearImageCacheAsync()
    {
        if (IsClearingImageCache) return;
        IsClearingImageCache = true;
        try
        {
            await ImageCacheService.Instance.ClearAllAsync();
            await RefreshCacheSizeAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("GeneralSettingsViewModel", $"[GeneralVM] ClearImageCache failed: {ex}");
        }
        finally { IsClearingImageCache = false; }
    }

    /// <summary>
    /// 缓存大小上限变化时：保存到 Preferences 并触发淘汰检查。
    /// </summary>
    partial void OnCacheSizeLimitMBChanged(int value)
    {
        AudioCacheService.Instance.SetCacheSizeLimitMB(value);
        UpdateCacheSizeLimitText();
    }

    private void UpdateCacheSizeLimitText()
    {
        CacheSizeLimitText = CacheSizeLimitMB >= 1024
            ? $"{CacheSizeLimitMB / 1024.0:F1} GB"
            : $"{CacheSizeLimitMB} MB";
    }

    /// <summary>
    /// 图片缓存上限变化时：保存到 Preferences 并触发清理。
    /// </summary>
    partial void OnImageCacheSizeLimitMBChanged(double value)
    {
        ImageCacheService.Instance.SetCacheSizeLimitMB((int)value);
        UpdateImageCacheLimitText();
    }

    private void UpdateImageCacheLimitText()
    {
        var mb = (int)ImageCacheSizeLimitMB;
        ImageCacheLimitText = mb >= 1024
            ? $"{mb / 1024.0:F1} GB"
            : $"{mb} MB";
    }

    /// <summary>
    /// 图片缓存有效期变化时：保存到 Preferences 并触发清理。
    /// </summary>
    partial void OnImageCacheAgeLimitDaysChanged(double value)
    {
        ImageCacheService.Instance.SetCacheAgeDays((int)value);
        UpdateImageCacheAgeText();
    }

    private void UpdateImageCacheAgeText()
    {
        var days = (int)ImageCacheAgeLimitDays;
        ImageCacheAgeText = $"{days} 天";
    }

    /// <summary>
    /// 恢复默认设置：清空所有 Preferences 并重置语言选项。
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
            Log.Debug("GeneralSettingsViewModel", $"[GeneralVM] ResetSettings failed: {ex}");
        }
    }

    /// <summary>
    /// 刷新缓存大小显示：扫描音乐缓存与图片缓存目录并格式化为可读字符串。
    /// </summary>
    public async Task RefreshCacheSizeAsync()
    {
        try
        {
            var (totalSize, imageSize) = await Task.Run(() =>
            {
                long audioSize = GetDirectorySize(Path.Combine(FileSystem.CacheDirectory, "music_cache"))
                               + GetDirectorySize(Path.Combine(FileSystem.CacheDirectory, "covers"));
                long imgSize = ImageCacheService.Instance.GetCacheSizeBytes();
                return (audioSize + imgSize, imgSize);
            });
            CacheSize = FormatSize(totalSize);
            ImageCacheSize = FormatSize(imageSize);
        }
        catch
        {
            CacheSize = "不可用";
            ImageCacheSize = "不可用";
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
