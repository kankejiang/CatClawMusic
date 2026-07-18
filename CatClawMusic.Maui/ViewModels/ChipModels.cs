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

    public static readonly Color AccentColor = Color.FromArgb("#8C7BFF");
    public static readonly Color TransparentColor = Colors.Transparent;

    public FilterChip(string key, string label, bool active)
    {
        FilterKey = key;
        Label = label;
        IsActive = active;
    }

    public Color BackgroundColor => IsActive ? AccentColor : TransparentColor;
    public Color TextColor => IsActive ? Colors.White : Color.FromArgb("#A8B4D8");
    public Color BorderColor => IsActive ? TransparentColor : Color.FromArgb("#33FFFFFF");

    partial void OnIsActiveChanged(bool value)
    {
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

    public static readonly Color AccentColor = Color.FromArgb("#8C7BFF");
    public static readonly Color TransparentColor = Colors.Transparent;

    public SortOption(string key, string label, bool active)
    {
        Key = key;
        Label = label;
        IsActive = active;
    }

    public Color TextColor => IsActive ? Color.FromArgb("#EAF0FF") : Color.FromArgb("#7A85B0");
    public Color BackgroundColor => IsActive ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#22FFFFFF");
    public Color BorderColor => IsActive ? AccentColor : TransparentColor;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(BorderColor));
    }
}
