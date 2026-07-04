using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 聊天角色转颜色转换器。
/// 根据角色名称（user / assistant / system）返回对应的气泡背景色。
/// </summary>
public class RoleToColorConverter : IValueConverter
{
    /// <summary>根据角色名称返回对应的颜色</summary>
    /// <param name="value">角色名称字符串（user/assistant/system）</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>角色对应的颜色；未知角色返回默认卡片背景色</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var role = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        var resources = Application.Current?.Resources;
        var user = resources?["ChipActiveColor"] as Color ?? Color.FromArgb("#8C7BFF");
        var assistant = resources?["CardBackgroundStrongColor"] as Color ?? Color.FromArgb("#30FFFFFF");
        var system = resources?["LikeColor"] as Color ?? Color.FromArgb("#FF7AAE");
        var fallback = resources?["CardBackgroundColor"] as Color ?? Color.FromArgb("#22FFFFFF");
        return role switch
        {
            "user" => user,
            "assistant" => assistant,
            "system" => system,
            _ => fallback
        };
    }

    /// <summary>反向转换不支持</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
