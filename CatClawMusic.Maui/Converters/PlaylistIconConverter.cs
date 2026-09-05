using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>
/// 播放列表名称转图标 Emoji 转换器。
/// 为系统内置播放列表（收藏歌曲/最近播放）返回对应图标，其余按常见歌单名匹配主题图标，
/// 兜底为猫爪（贴合应用萌系主题；历史版本此处误用篮球码位 🏀，已修正）。
/// </summary>
public class PlaylistIconConverter : IValueConverter
{
    /// <summary>根据播放列表名称返回对应的图标 Emoji</summary>
    /// <param name="value">播放列表名称</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">额外参数（未使用）</param>
    /// <param name="culture">区域性信息</param>
    /// <returns>对应图标 Emoji 字符串</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        return name switch
        {
            "收藏歌曲" => "\u2665",      // ♥
            "最近播放" => "\U0001f552",  // 🕒

            // 常见歌单名 → 贴题图标（小写比较，忽略首尾空格）
            _ => MatchThemeIcon(name)
        };
    }

    /// <summary>按常见歌单名匹配图标，未命中时返回默认猫爪图标</summary>
    private static string MatchThemeIcon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DefaultIcon;

        var n = name.Trim().ToLowerInvariant();
        if (n.Contains("喜欢") || n.Contains("收藏") || n.Contains("红心") || n.Contains("love") || n.Contains("fav"))
            return "\u2665";             // ♥
        if (n.Contains("最近") || n.Contains("播放") || n.Contains("历史") || n.Contains("recent"))
            return "\U0001f552";         // 🕒
        if (n.Contains("晚安") || n.Contains("睡眠") || n.Contains("夜") || n.Contains("sleep"))
            return "\U0001f319";         // 🌙
        if (n.Contains("运动") || n.Contains("跑步") || n.Contains("健身") || n.Contains("run") || n.Contains("sport"))
            return "\U0001f3c3";         // 🏃
        if (n.Contains("雨") || n.Contains("白噪音") || n.Contains("助眠") || n.Contains("rain") || n.Contains("asmr"))
            return "\U0001f327";         // 🌧
        if (n.Contains("旅行") || n.Contains("公路") || n.Contains("travel") || n.Contains("road"))
            return "\u2708";             // ✈
        if (n.Contains("学习") || n.Contains("工作") || n.Contains("专注") || n.Contains("study") || n.Contains("focus"))
            return "\U0001f4da";         // 📚
        if (n.Contains("摇滚") || n.Contains("rock"))
            return "\U0001f3b8";         // 🎸
        if (n.Contains("钢琴") || n.Contains("古典") || n.Contains("piano") || n.Contains("classical"))
            return "\U0001f3b9";         // 🎹
        if (n.Contains("爵士") || n.Contains("jazz"))
            return "\U0001f3b7";         // 🎺
        if (n.Contains("流行") || n.Contains("pop"))
            return "\U0001f3b6";         // 🎶
        if (n.Contains("粤语") || n.Contains("老歌") || n.Contains("怀旧") || n.Contains("经典"))
            return "\U0001f4bf";         // 💿 光盘
        return DefaultIcon;
    }

    /// <summary>默认图标：猫爪（贴合应用萌系主题）</summary>
    private static string DefaultIcon => "\U0001f43e"; // 🐾

    /// <summary>反向转换不支持</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
