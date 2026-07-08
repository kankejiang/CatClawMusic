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

    /// <summary>设置页语言下拉索引 -> 文化名 的映射（须与 GeneralSettingsViewModel.LanguageOptions 顺序一致）。</summary>
    private static readonly Dictionary<int, string> IndexToCulture = new()
    {
        [0] = "zh-CN",
        [1] = "zh-TW",
        [2] = "en",
        [3] = "ja",
        [4] = "ko",
        [5] = "fr",
        [6] = "de",
        [7] = "es",
        [8] = "ru",
        [9] = "pt",
        [10] = "it",
        [11] = "ar",
        [12] = "hi",
        [13] = "bn",
        [14] = "ta",
        [15] = "te",
        [16] = "mr",
        [17] = "gu",
        [18] = "kn",
        [19] = "ml",
        [20] = "id",
        [21] = "ms",
        [22] = "th",
        [23] = "vi",
        [24] = "nl",
        [25] = "pl",
        [26] = "tr",
        [27] = "uk",
        [28] = "sv",
        [29] = "nb",
        [30] = "da",
        [31] = "fi",
        [32] = "cs",
        [33] = "hu",
        [34] = "ro",
        [35] = "el",
        [36] = "he",
        [37] = "sk",
        [38] = "hr",
        [39] = "sr",
        [40] = "bg",
        [41] = "fil",
        [42] = "ur",
        [43] = "fa",
        [44] = "sw",
        [45] = "ca",
        [46] = "lv",
        [47] = "lt",
        [48] = "et",
        [49] = "sl",
        [50] = "is"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>索引器：返回指定 key 的本地化字符串；缺失时回退为 key 本身（便于发现未翻译项）。</summary>
    public string this[string key] =>
        _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>应用启动时调用：读取已保存语言并应用到当前线程与应用文化。</summary>
    public static void Initialize()
    {
        ApplyCulture(GetSavedCultureName());
    }

    /// <summary>读取已保存的文化名，无效时回退到默认。</summary>
    public static string GetSavedCultureName()
    {
        var saved = Preferences.Default.Get(PreferenceKey, DefaultCulture);
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
        ApplyCulture(cultureName);

        // 通知所有绑定重新求值（索引器绑定依赖此项刷新）
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
