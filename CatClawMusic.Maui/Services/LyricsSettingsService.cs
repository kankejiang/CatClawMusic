using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 歌词显示设置：管理歌词模式、对齐方式、字体大小。
/// 设置持久化到 Preferences，应用启动后单例可用。
/// </summary>
public class LyricsSettingsService
{
    /// <summary>歌词模式枚举</summary>
    public enum Mode
    {
        /// <summary>逐行高亮</summary>
        Line = 0,
        /// <summary>逐字渐进填充（后期支持）</summary>
        Word = 1
    }

    /// <summary>对齐方式枚举</summary>
    public enum Alignment
    {
        Left = 0,
        Center = 1,
        Right = 2
    }

    private const string KeyMode = "lyrics_mode";
    private const string KeyAlignment = "lyrics_alignment";
    private const string KeyFontSize = "lyrics_font_size";

    /// <summary>默认字体大小（当前行）</summary>
    public const double DefaultFontSize = 26;
    /// <summary>最小字体大小</summary>
    public const double MinFontSize = 18;
    /// <summary>最大字体大小</summary>
    public const double MaxFontSize = 38;

    public Mode LyricsMode
    {
        get => (Mode)Preferences.Get(KeyMode, (int)Mode.Line);
        set => Preferences.Set(KeyMode, (int)value);
    }

    public Alignment LyricsAlignment
    {
        get => (Alignment)Preferences.Get(KeyAlignment, (int)Alignment.Center);
        set => Preferences.Set(KeyAlignment, (int)value);
    }

    public double FontSize
    {
        get => Preferences.Get(KeyFontSize, DefaultFontSize);
        set => Preferences.Set(KeyFontSize, Math.Clamp(value, MinFontSize, MaxFontSize));
    }

    /// <summary>歌词对齐方式转换为 MAUI TextAlignment</summary>
    public TextAlignment ToTextAlignment() => LyricsAlignment switch
    {
        Alignment.Left => TextAlignment.Start,
        Alignment.Right => TextAlignment.End,
        _ => TextAlignment.Center
    };

    /// <summary>歌词对齐方式转换为 LayoutOptions</summary>
    public LayoutOptions ToLayoutOptions() => LyricsAlignment switch
    {
        Alignment.Left => LayoutOptions.Start,
        Alignment.Right => LayoutOptions.End,
        _ => LayoutOptions.Center
    };

    private static LyricsSettingsService? _instance;
    public static LyricsSettingsService Instance => _instance ??= new LyricsSettingsService();
}
