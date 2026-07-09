using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 将 MauiImage 资源名（如 "ic_play"）在运行时正确转换为 ImageSource。
/// </summary>
/// <remarks>
/// XAML 字面量 Source="ic_play" 能工作是因为编译期 ImageSourceConverter。
/// 绑定 string 到 ImageSource 在某些版本不会自动解析，且 ImageSource.FromFile
/// 会把资源名当成文件路径导致 E_NETWORK_ERROR。此 helper 显式走 Converter。
/// </remarks>
public static class ImageSourceHelper
{
    private static readonly ImageSourceConverter _converter = new();

    /// <summary>
    /// 已有 _light 变体的图标白名单。这些图标在浅色模式下使用深色填充 (#1B2140)，
    /// 在深色模式下使用白色填充 (#FFFFFF/#C8C8C8)。
    /// 不在此列表中的图标（如 ic_favorite 使用彩色）在两种模式下都使用原始版本。
    /// </summary>
    private static readonly HashSet<string> _themedIcons = new()
    {
        "ic_play", "ic_pause",
        "ic_skip_previous", "ic_skip_next",
        "ic_repeat_all", "ic_repeat_one", "ic_shuffle",
    };

    public static ImageSource? FromName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // Windows 上 MauiImage 生成的是 .png，需要带扩展名才能被 FromFile 解析；
        // Android/iOS 上扩展名不影响 drawable 资源查找。
        var fileName = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : name + ".png";
        return ImageSource.FromFile(fileName);
    }

    /// <summary>
    /// 主题感知版本：浅色模式下对白名单图标自动使用 _light 变体。
    /// 用于 ViewModel 中绑定的图标源，在主题切换时需调用方刷新。
    /// </summary>
    public static ImageSource? FromNameThemed(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && _themedIcons.Contains(name))
        {
            return FromName(name + "_light");
        }
        return FromName(name);
    }
}
