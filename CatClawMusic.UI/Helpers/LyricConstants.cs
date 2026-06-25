namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 歌词渲染相关共享常量，供 LyricRendererView 和各歌词 Fragment 复用。
/// </summary>
public static class LyricConstants
{
    /// <summary>歌词颜色预设（与旧版索引存储兼容，用于迁移）</summary>
    public static readonly string[] PresetColorHex =
        { "#FFFFFFFF", "#FF000000", "#FFFFEB3B", "#FF69F0AE", "#FFFF80AB", "#FF64B5F6", "#FFFFAB40", "#FFFF6E6E", "#FFCE93D8", "#FF4DD0E1" };

    /// <summary>非高亮歌词颜色预设</summary>
    public static readonly string[] InactivePresetHex =
        { "#CCBBBBBB", "#CC555555", "#CC000000", "#DDDDDDDD", "#CC90A4AE", "#CCB39DDB", "#CCBDBDBD", "#CC78909C" };

    /// <summary>全屏歌词页背景遮罩色（较透明：0x99/0x33）</summary>
    public static readonly string[] FullScreenBgColorHex = { "#99F0EBE3", "#990F0D16", "#33000000" };

    /// <summary>播放页背景遮罩色（较不透明：0xCC/0x00）</summary>
    public static readonly string[] PlayerBgColorHex = { "#CCF0EBE3", "#CC0F0D16", "#00000000" };
}
