namespace CatClawMusic.Core.Models;

/// <summary>
/// 音频文件标签信息（读写用）。字段对应 Lyrico 元数据编辑页的常用字段：
/// 标题/艺人/专辑/专辑艺人/年份/流派/音轨号/碟号/作曲/作词/注释/版权/内嵌歌词/封面/自定义标签。
/// <para>FilePath 为 SAF content:// URI 或本地路径（由宿主实现决定）。</para>
/// </summary>
public class AudioTagInfo
{
    /// <summary>文件 URI（content:// 或绝对路径）</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>艺人（可多个，用 " / " 分隔）</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>专辑</summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>专辑艺人</summary>
    public string AlbumArtist { get; set; } = string.Empty;

    /// <summary>年份</summary>
    public string Year { get; set; } = string.Empty;

    /// <summary>流派</summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>音轨号</summary>
    public string TrackNumber { get; set; } = string.Empty;

    /// <summary>碟号</summary>
    public string DiscNumber { get; set; } = string.Empty;

    /// <summary>作曲（可多个，用 " / " 分隔）</summary>
    public string Composer { get; set; } = string.Empty;

    /// <summary>作词</summary>
    public string Lyricist { get; set; } = string.Empty;

    /// <summary>注释</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>版权</summary>
    public string Copyright { get; set; } = string.Empty;

    /// <summary>自定义标签（ID3 TXXX / 键 → 值）</summary>
    public Dictionary<string, string> CustomTags { get; set; } = new();

    /// <summary>内嵌歌词（LRC 文本）</summary>
    public string? Lyrics { get; set; }

    /// <summary>内嵌封面（JPEG/PNG 原始字节），无封面为 null</summary>
    public byte[]? Cover { get; set; }

    /// <summary>时长（毫秒）</summary>
    public long DurationMs { get; set; }

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>音频比特率（kbps）</summary>
    public int Bitrate { get; set; }

    /// <summary>采样率（Hz）</summary>
    public int SampleRate { get; set; }

    /// <summary>声道数</summary>
    public int Channels { get; set; }

    /// <summary>文件扩展名（含点，如 .mp3）</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>文件显示名（含扩展名）</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// 音频标签编辑请求。只有非 null 的字段才会被写入；null 表示"保持原样"。
/// </summary>
public class AudioTagEdit
{
    /// <summary>标题（null = 不改）</summary>
    public string? Title { get; set; }

    /// <summary>艺人（null = 不改）</summary>
    public string? Artist { get; set; }

    /// <summary>专辑（null = 不改）</summary>
    public string? Album { get; set; }

    /// <summary>专辑艺人（null = 不改）</summary>
    public string? AlbumArtist { get; set; }

    /// <summary>年份（null = 不改）</summary>
    public string? Year { get; set; }

    /// <summary>流派（null = 不改）</summary>
    public string? Genre { get; set; }

    /// <summary>音轨号（null = 不改）</summary>
    public string? TrackNumber { get; set; }

    /// <summary>碟号（null = 不改）</summary>
    public string? DiscNumber { get; set; }

    /// <summary>作曲（null = 不改；空字符串 = 清除）</summary>
    public string? Composer { get; set; }

    /// <summary>作词（null = 不改；空字符串 = 清除）</summary>
    public string? Lyricist { get; set; }

    /// <summary>注释（null = 不改；空字符串 = 清除）</summary>
    public string? Comment { get; set; }

    /// <summary>版权（null = 不改；空字符串 = 清除）</summary>
    public string? Copyright { get; set; }

    /// <summary>自定义标签（键 → 新值，null = 不改；值为空字符串表示移除该键）</summary>
    public Dictionary<string, string>? CustomTags { get; set; }

    /// <summary>内嵌歌词（null = 不改；空字符串 = 清除）</summary>
    public string? Lyrics { get; set; }

    /// <summary>封面（null = 不改；空数组 = 清除）</summary>
    public byte[]? Cover { get; set; }
}
