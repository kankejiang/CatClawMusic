using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 轻量多语言服务：基于 .resx 资源 + ResourceManager，支持运行时切换并通知所有绑定刷新。
/// 不依赖 CommunityToolkit.Maui，纯标准库实现。
/// XAML 中通过索引器绑定使用：
///   Text="{Binding Source={x:Static services:LocalizationService.Instance}, Path=[Key]}"
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static readonly ResourceManager _resourceManager =
        new("CatClawMusic.Maui.Resources.AppResources", typeof(LocalizationService).Assembly);

    private static readonly LocalizationService _instance = new();

    /// <summary>全局单例，供 XAML 的 x:Static 绑定使用。</summary>
    public static LocalizationService Instance => _instance;

    /// <summary>Preferences 中保存语言文化名的键。</summary>
    public const string PreferenceKey = "AppLanguage";

    /// <summary>默认（中性）文化名，对应 AppResources.resx。</summary>
    public const string DefaultCulture = "zh-CN";

    /// <summary>"跟随系统"的保存值，表示使用系统语言。</summary>
    public const string SystemCulture = "system";

    /// <summary>设置页语言下拉索引 -> 文化名 的映射（须与 GeneralSettingsViewModel.LanguageOptions 顺序一致）。</summary>
    private static readonly Dictionary<int, string> IndexToCulture = new()
    {
        [0] = SystemCulture,
        [1] = "zh-CN",
        [2] = "zh-TW",
        [3] = "en",
        [4] = "ja",
        [5] = "ko",
        [6] = "fr",
        [7] = "de",
        [8] = "es",
        [9] = "ru",
        [10] = "pt",
        [11] = "it",
        [12] = "ar",
        [13] = "hi",
        [14] = "bn",
        [15] = "ta",
        [16] = "te",
        [17] = "mr",
        [18] = "gu",
        [19] = "kn",
        [20] = "ml",
        [21] = "id",
        [22] = "ms",
        [23] = "th",
        [24] = "vi",
        [25] = "nl",
        [26] = "pl",
        [27] = "tr",
        [28] = "uk",
        [29] = "sv",
        [30] = "nb",
        [31] = "da",
        [32] = "fi",
        [33] = "cs",
        [34] = "hu",
        [35] = "ro",
        [36] = "el",
        [37] = "he",
        [38] = "sk",
        [39] = "hr",
        [40] = "sr",
        [41] = "bg",
        [42] = "fil",
        [43] = "ur",
        [44] = "fa",
        [45] = "sw",
        [46] = "ca",
        [47] = "lv",
        [48] = "lt",
        [49] = "et",
        [50] = "sl",
        [51] = "is"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>索引器：返回指定 key 的本地化字符串；缺失时回退为 key 本身（便于发现未翻译项）。</summary>
    public string this[string key] =>
        _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>应用启动时调用：读取已保存语言并应用到当前线程与应用文化。</summary>
    public static void Initialize()
    {
        var saved = GetSavedCultureName();
        if (saved == SystemCulture)
        {
            // 跟随系统：不覆盖系统默认文化
            return;
        }
        ApplyCulture(saved);
    }

    /// <summary>读取已保存的文化名，"system" 表示跟随系统语言。</summary>
    public static string GetSavedCultureName()
    {
        var saved = Preferences.Default.Get(PreferenceKey, SystemCulture);
        if (saved == SystemCulture) return SystemCulture;
        return CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Any(c => c.Name == saved)
            ? saved
            : DefaultCulture;
    }

    /// <summary>读取已保存语言对应的设置页下拉索引。</summary>
    public static int GetSavedCultureIndex()
    {
        var name = GetSavedCultureName();
        foreach (var kv in IndexToCulture)
            if (kv.Value == name) return kv.Key;
        return 0;
    }

    /// <summary>将指定文化名应用到当前线程（并尝试设置 MAUI 应用请求文化）。</summary>
    public static void ApplyCulture(string cultureName)
    {
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch
        {
            var fallback = new CultureInfo(DefaultCulture);
            CultureInfo.CurrentCulture = fallback;
            CultureInfo.CurrentUICulture = fallback;
        }
    }

    /// <summary>
    /// 根据设置页语言索引切换语言：持久化、应用文化并通知所有绑定（含索引器）刷新。
    /// </summary>
    /// <param name="index">GeneralSettingsViewModel.LanguageOptions 的索引。</param>
    public void SetLanguageByIndex(int index)
    {
        var cultureName = IndexToCulture.TryGetValue(index, out var c) ? c : DefaultCulture;

        Preferences.Default.Set(PreferenceKey, cultureName);

        if (cultureName == SystemCulture)
        {
            // 跟随系统：恢复系统默认文化
            var systemCulture = CultureInfo.InstalledUICulture ?? new CultureInfo(DefaultCulture);
            CultureInfo.CurrentCulture = systemCulture;
            CultureInfo.CurrentUICulture = systemCulture;
        }
        else
        {
            ApplyCulture(cultureName);
        }

        // 通知所有绑定重新求值（索引器绑定依赖此项刷新）
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
