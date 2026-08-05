using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 来源筛选 chip（与艺术家/专辑页完全一致的实现）。
/// </summary>
public partial class FilterChip : ObservableObject
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
        SubscribeTheme();
    }

    ~FilterChip()
    {
        UnsubscribeTheme();
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

    // === 主题切换实时刷新（从 Application.Current.Resources 重新读） ===

    private void SubscribeTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += OnAppThemeChanged;
    }

    private void UnsubscribeTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged -= OnAppThemeChanged;
    }

    private void OnAppThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        // 主题色资源已由 ThemeService.ApplyTheme() 在事件触发前重写完毕，
        // 此时重新触发 PropertyChanged 即拿新值。
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }
}

/// <summary>
/// 排序选项 chip（与艺术家/专辑页完全一致的实现）。
/// </summary>
public partial class SortOption : ObservableObject
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
        SubscribeTheme();
    }

    ~SortOption()
    {
        UnsubscribeTheme();
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

    // === 主题切换实时刷新 ===

    private void SubscribeTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged += OnAppThemeChanged;
    }

    private void UnsubscribeTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeChanged -= OnAppThemeChanged;
    }

    private void OnAppThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }
}
