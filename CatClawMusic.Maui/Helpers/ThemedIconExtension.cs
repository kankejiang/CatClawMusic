using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// XAML 标记扩展：按当前主题返回已解析的 <see cref="ImageSource"/>。
/// 用于替代 WinUI 上经常解析失败的 AppThemeBinding 字符串图标源。
/// </summary>
/// <remarks>
/// 用法示例（主题图标）：
///   &lt;Image Source="{helpers:ThemedIcon Light=ic_search_light, Dark=ic_search}" /&gt;
/// 非主题图标（始终使用原始版本）：
///   &lt;Image Source="{helpers:ThemedIcon Name=ic_add, Original=True}" /&gt;
/// </remarks>
[ContentProperty(nameof(Name))]
public class ThemedIconExtension : IMarkupExtension<ImageSource>
{
    /// <summary>非主题图标名称，或作为 Light/Dark 未指定时的回退。</summary>
    public string? Name { get; set; }

    /// <summary>浅色模式下使用的图标名称。</summary>
    public string? Light { get; set; }

    /// <summary>深色模式下使用的图标名称。</summary>
    public string? Dark { get; set; }

    /// <summary>为 true 时始终使用原始版本（不自动切换 _light 变体）。</summary>
    public bool Original { get; set; }

    public ImageSource? ProvideValue(IServiceProvider serviceProvider)
    {
        var source = ResolveImageSource();
        if (source == null) return null;

        // 动态主题刷新：标记扩展只在 XAML 加载时求值一次，深浅模式切换后已加载页面不会重建。
        // 这里挂到全局 RequestedThemeChanged，主题变化时给目标 Image 重新求值图标；
        // Image 卸载时退订，防止事件泄漏。
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is Image image &&
            Application.Current != null)
        {
            var refresh = new EventHandler<AppThemeChangedEventArgs>((_, _) =>
            {
                image.Source = ResolveImageSource();
            });
            Application.Current.RequestedThemeChanged += refresh;
            image.Unloaded += (_, _) => Application.Current.RequestedThemeChanged -= refresh;
        }

        return source;
    }

    /// <summary>按当前深浅模式解析图标名并转为 ImageSource</summary>
    private ImageSource? ResolveImageSource()
    {
        string? selectedName;
        if (!string.IsNullOrEmpty(Light) || !string.IsNullOrEmpty(Dark))
        {
            bool isLight = Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light;
            selectedName = isLight ? Light : Dark;
        }
        else
        {
            selectedName = Name;
        }

        if (string.IsNullOrEmpty(selectedName)) return null;

        return Original
            ? ImageSourceHelper.FromNameOriginal(selectedName)
            : ImageSourceHelper.FromNameThemed(selectedName);
    }

    object? IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
