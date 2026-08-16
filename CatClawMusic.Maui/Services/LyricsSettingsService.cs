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
    private const string KeyAlignmentMigrated = "lyrics_alignment_migrated_v2";
    private const string KeyShowTranslation = "lyrics_show_translation";
    private const string KeyShowRoma = "lyrics_show_roma";
    private const string KeySource = "lyrics_source";

    // 桌面歌词设置键
    private const string KeyDesktopEnabled = "desktop_lyric_enabled";
    private const string KeyDesktopFontSize = "desktop_lyric_font_size";
    private const string KeyDesktopTextColor = "desktop_lyric_text_color";
    private const string KeyDesktopHighlightColor = "desktop_lyric_highlight_color";
    private const string KeyDesktopLocked = "desktop_lyric_locked";
    private const string KeyDesktopBgOpacity = "desktop_lyric_bg_opacity";
    private const string KeyDesktopPosY = "desktop_lyric_pos_y";
    private const string KeyDesktopMode = "desktop_lyric_mode";

    /// <summary>桌面歌词显示模式</summary>
    public enum DesktopMode
    {
        /// <summary>单行：仅显示当前歌词行</summary>
        Single = 0,
        /// <summary>双行：当前行 + 下一行（主流播放器默认形态）</summary>
        Double = 1
    }

    /// <summary>默认字体大小（当前行）</summary>
    public const double DefaultFontSize = 22;
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
        get
        {
            // 一次性迁移：旧版本默认值为 Center，新版本改为 Left。
            // 已安装用户的首选项可能保存了旧的默认值 Center，需重置为新的默认值 Left。
            if (!Preferences.Get(KeyAlignmentMigrated, false))
            {
                Preferences.Set(KeyAlignmentMigrated, true);
                Preferences.Set(KeyAlignment, (int)Alignment.Left);
            }
            return (Alignment)Preferences.Get(KeyAlignment, (int)Alignment.Left);
        }
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

    /// <summary>是否显示歌词译文（lx-music 扩展歌词 tlrc 流 / 行内译文）</summary>
    public bool ShowTranslation
    {
        get => Preferences.Get(KeyShowTranslation, true);
        set => Preferences.Set(KeyShowTranslation, value);
    }

    /// <summary>是否显示歌词罗马音（lx-music 扩展歌词 rlrc 流）</summary>
    public bool ShowRoma
    {
        get => Preferences.Get(KeyShowRoma, true);
        set => Preferences.Set(KeyShowRoma, value);
    }

    /// <summary>歌词来源：在线 / 本地（自动/内嵌/外挂）。
    /// 在线模式启用网易云三流歌词（译文/罗马音由 ShowTranslation/ShowRoma 控制）；
    /// 本地模式译文/罗马音由歌词文件本身决定。切换时同步到 Core 的 LyricsService.LyricSourceMode。</summary>
    public Core.Services.LyricSourceMode LyricSource
    {
        get
        {
            var mode = (Core.Services.LyricSourceMode)Preferences.Get(KeySource, (int)Core.Services.LyricSourceMode.Online);
            Core.Services.LyricsService.LyricSourceMode = mode; // 同步到 Core（读取时也校正一次）
            return mode;
        }
        set
        {
            Preferences.Set(KeySource, (int)value);
            Core.Services.LyricsService.LyricSourceMode = value;
        }
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

    /// <summary>桌面歌词显示模式（单行 / 双行）</summary>
    public DesktopMode DesktopLyricMode
    {
        get => (DesktopMode)Preferences.Get(KeyDesktopMode, (int)DesktopMode.Single);
        set => Preferences.Set(KeyDesktopMode, (int)value);
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
