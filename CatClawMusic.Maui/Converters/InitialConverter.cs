using System.Globalization;

namespace CatClawMusic.Maui.Converters;

/// <summary>将标题文本转换为首字符（大写），用于封面占位显示</summary>
public class InitialConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrEmpty(text)) return "♪";
        return text.Trim()[0].ToString().ToUpperInvariant();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>检查封面路径是否有效（非空且文件存在）</summary>
public class CoverArtConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
            return true;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>根据专辑/艺术家 ID 生成一致的占位颜色</summary>
public class PlaceholderColorConverter : IValueConverter
{
    private static readonly string[] Palettes = {
        "#8C7BFF", "#FF7AAE", "#55D6FF", "#A78BFA",
        "#5EEAD4", "#FBBF24", "#818CF8", "#F472B6"
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = value is int i ? i : 0;
        var index = Math.Abs(id) % Palettes.Length;
        return Color.FromArgb(Palettes[index]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>将播放次数格式化为显示文本</summary>
public class PlaybackCountConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
            return $"{count} 次";
        return "0 次";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>将艺术家名称转换为索引字母（A-Z 或 #）</summary>
public class NameToLetterConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrEmpty(name))
        {
            var c = name.Trim().ToUpperInvariant()[0];
            if (c >= 'A' && c <= 'Z') return c.ToString();
            if (c >= 0x4E00 && c <= 0x9FFF) return "中";
        }
        return "#";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
