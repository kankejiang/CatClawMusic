using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>主题色 chip 接口：主题切换时由 <see cref="ChipThemeBroadcast"/> 广播刷新。</summary>
internal interface IThemedChip
{
    void RefreshThemeColors();
}

/// <summary>
/// 主题切换广播（弱引用版）。原实现由每个 chip 构造时订阅
/// <c>Application.Current.RequestedThemeChanged</c>、终结器退订——事件源强引用委托、
/// 委托强引用 chip，chip 永不回收、终结器永不执行（死循环泄漏），每进入一次
/// 专辑/艺术家页就永久泄漏一组 chip。现改为：静态弱引用列表 + 只注册一次的静态处理器，
/// 主题切换时通知仍存活的 chip，已回收的自动剔除，彻底消除泄漏。
/// </summary>
internal static class ChipThemeBroadcast
{
    private static readonly object Sync = new();
    private static readonly List<WeakReference<IThemedChip>> Chips = new();
    private static bool _subscribed;

    public static void Track(IThemedChip chip)
    {
        lock (Sync)
        {
            Chips.Add(new WeakReference<IThemedChip>(chip));
            if (_subscribed) return;
            if (Application.Current != null)
                Application.Current.RequestedThemeChanged += OnAppThemeChanged;
            _subscribed = true;
        }
    }

    private static void OnAppThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        lock (Sync)
        {
            // 主题色资源已由 ThemeService.ApplyTheme() 在事件触发前重写完毕，
            // 通知仍存活的 chip 重新触发 PropertyChanged 即拿新值。
            for (var i = Chips.Count - 1; i >= 0; i--)
            {
                if (Chips[i].TryGetTarget(out var chip))
                    chip.RefreshThemeColors();
                else
                    Chips.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// 来源筛选 chip（与艺术家/专辑页完全一致的实现）。
/// </summary>
public partial class FilterChip : ObservableObject, IThemedChip
{
    public string FilterKey { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;

    public FilterChip(string key, string label, bool active)
    {
        FilterKey = key;
        Label = label;
        IsActive = active;
        ChipThemeBroadcast.Track(this);
    }

    // 主题色从 Application.Current.Resources 实时读取，跟随 ThemeService 主题切换。
    private static Color Accent() => (Color)(Application.Current?.Resources["PrimaryColor"] ?? Color.FromArgb("#8C7BFF"));
    private static Color AccentDark() => (Color)(Application.Current?.Resources["PrimaryDarkColor"] ?? Color.FromArgb("#6250F6"));
    private static Color AccentLight() => (Color)(Application.Current?.Resources["PrimaryLightColor"] ?? Color.FromArgb("#B7AEFF"));
    private static Color TextPrimary() => (Color)(Application.Current?.Resources["TextPrimaryColor"] ?? Colors.White);
    private static Color TextHint() => (Color)(Application.Current?.Resources["TextHintColor"] ?? Color.FromArgb("#8D93B7"));

    public Color BackgroundColor => IsActive
        ? Accent()                                                          // 选中：主色
        : AccentLight().WithAlpha(0.20f);                                  // 未选：主色淡
    public Color TextColor => IsActive ? Colors.White : TextHint();          // 选中：白底；未选：主文字色
    public Color BorderColor => IsActive
        ? AccentDark()                                                      // 选中：主色描边
        : AccentLight().WithAlpha(0.35f);                                  // 未选：主色淡描边

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }

    void IThemedChip.RefreshThemeColors()
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }
}

/// <summary>
/// 排序选项 chip（与艺术家/专辑页完全一致的实现）。
/// </summary>
public partial class SortOption : ObservableObject, IThemedChip
{
    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;

    public SortOption(string key, string label, bool active)
    {
        Key = key;
        Label = label;
        IsActive = active;
        ChipThemeBroadcast.Track(this);
    }

    // 主题色从 Application.Current.Resources 实时读取，跟随 ThemeService 主题切换。
    private static Color Accent() => (Color)(Application.Current?.Resources["PrimaryColor"] ?? Color.FromArgb("#8C7BFF"));
    private static Color AccentDark() => (Color)(Application.Current?.Resources["PrimaryDarkColor"] ?? Color.FromArgb("#6250F6"));
    private static Color AccentLight() => (Color)(Application.Current?.Resources["PrimaryLightColor"] ?? Color.FromArgb("#B7AEFF"));
    private static Color TextHint() => (Color)(Application.Current?.Resources["TextHintColor"] ?? Color.FromArgb("#8D93B7"));

    public Color BackgroundColor => IsActive
        ? Accent()                                                          // 选中：主色
        : AccentLight().WithAlpha(0.18f);                                  // 未选：主色淡背景
    public Color TextColor => IsActive ? Colors.White : TextHint();          // 选中：白底；未选：主文字色
    public Color BorderColor => IsActive
        ? AccentDark()                                                      // 选中：主色描边
        : AccentLight().WithAlpha(0.30f);                                  // 未选：主色淡描边

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }

    void IThemedChip.RefreshThemeColors()
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }
}
