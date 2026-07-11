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
    private const string KeyRemoveEmptyLines = "lyrics_remove_empty_lines";

    // 桌面歌词设置键
    private const string KeyDesktopEnabled = "desktop_lyric_enabled";
    private const string KeyDesktopFontSize = "desktop_lyric_font_size";
    private const string KeyDesktopTextColor = "desktop_lyric_text_color";
    private const string KeyDesktopHighlightColor = "desktop_lyric_highlight_color";
    private const string KeyDesktopLocked = "desktop_lyric_locked";
    private const string KeyDesktopBgOpacity = "desktop_lyric_bg_opacity";
    private const string KeyDesktopPosY = "desktop_lyric_pos_y";

    /// <summary>默认字体大小（当前行）</summary>
    public const double DefaultFontSize = 26;
    /// <summary>最小字体大小</summary>
    public const double MinFontSize = 18;
    /// <summary>最大字体大小</summary>
    public const double MaxFontSize = 38;

    /// <summary>桌面歌词默认字号</summary>
    public const double DesktopDefaultFontSize = 20;
    /// <summary>桌面歌词最小字号</summary>
    public const double DesktopMinFontSize = 14;
    /// <summary>桌面歌词最大字号</summary>
    public const double DesktopMaxFontSize = 32;

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

    /// <summary>是否智能删除空行（让歌词更紧凑）</summary>
    public bool RemoveEmptyLines
    {
        get => Preferences.Get(KeyRemoveEmptyLines, true);
        set => Preferences.Set(KeyRemoveEmptyLines, value);
    }

    // ═══════════════════════════════════════
    // 桌面歌词设置
    // ═══════════════════════════════════════

    /// <summary>桌面歌词是否开启</summary>
    public bool DesktopLyricEnabled
    {
        get => Preferences.Get(KeyDesktopEnabled, false);
        set => Preferences.Set(KeyDesktopEnabled, value);
    }

    /// <summary>桌面歌词字号</summary>
    public double DesktopFontSize
    {
        get => Preferences.Get(KeyDesktopFontSize, DesktopDefaultFontSize);
        set => Preferences.Set(KeyDesktopFontSize, Math.Clamp(value, DesktopMinFontSize, DesktopMaxFontSize));
    }

    /// <summary>桌面歌词未唱文字颜色（ARGB hex 字符串）</summary>
    public string DesktopTextColor
    {
        get => Preferences.Get(KeyDesktopTextColor, "#B3FFFFFF");
        set => Preferences.Set(KeyDesktopTextColor, value);
    }

    /// <summary>桌面歌词已唱高亮颜色（ARGB hex 字符串）</summary>
    public string DesktopHighlightColor
    {
        get => Preferences.Get(KeyDesktopHighlightColor, "#FFFFE082");
        set => Preferences.Set(KeyDesktopHighlightColor, value);
    }

    /// <summary>桌面歌词是否锁定位置（锁定后不可拖动）</summary>
    public bool DesktopLocked
    {
        get => Preferences.Get(KeyDesktopLocked, false);
        set => Preferences.Set(KeyDesktopLocked, value);
    }

    /// <summary>桌面歌词背景透明度（0~1，0为完全透明）</summary>
    public double DesktopBgOpacity
    {
        get => Preferences.Get(KeyDesktopBgOpacity, 0.3);
        set => Preferences.Set(KeyDesktopBgOpacity, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>桌面歌词垂直位置（0~1，屏幕高度比例）</summary>
    public double DesktopPosY
    {
        get => Preferences.Get(KeyDesktopPosY, 0.75);
        set => Preferences.Set(KeyDesktopPosY, Math.Clamp(value, 0.1, 0.95));
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
