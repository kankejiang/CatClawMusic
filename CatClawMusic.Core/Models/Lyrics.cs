namespace CatClawMusic.Core.Models;

/// <summary>
/// LRC 歌词模型
/// </summary>
public class LrcLyrics
{
    /// <summary>
    /// 歌词元数据（ti、ar、al 等）
    /// </summary>
    public LrcMetadata Metadata { get; set; } = new();
    
    /// <summary>
    /// 歌词行列表
    /// </summary>
    public List<LrcLyricLine> Lines { get; set; } = new();

    /// <summary>
    /// 是否有按行对齐方式（TTML/AMLL 的 role 属性决定）
    /// 如果为 true，UI 层应使用每行的 Alignment 字段；
    /// 如果为 false，UI 层应使用全局 _lyricAlignment 设置。
    /// </summary>
    public bool HasPerLineAlignment { get; set; }
}

/// <summary>
/// LRC 歌词元数据
/// </summary>
public class LrcMetadata
{
    /// <summary>
    /// 歌曲标题 [ti:...]
    /// </summary>
    public string? Title { get; set; }
    
    /// <summary>
    /// 艺术家 [ar:...]
    /// </summary>
    public string? Artist { get; set; }
    
    /// <summary>
    /// 专辑 [al:...]
    /// </summary>
    public string? Album { get; set; }
    
    /// <summary>
    /// 歌词作者 [by:...]
    /// </summary>
    public string? Author { get; set; }
    
    /// <summary>
    /// 制作 [re:...]
    /// </summary>
    public string? Maker { get; set; }
    
    /// <summary>
    /// 版本 [ve:...]
    /// </summary>
    public string? Version { get; set; }
}

/// <summary>
/// LRC 歌词行
/// </summary>
public class LrcLyricLine
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Translation { get; set; }
    public List<WordTimestamp>? WordTimestamps { get; set; }
    
    /// <summary>
    /// 歌词行对齐方式（用于合唱/对唱布局）
    /// 0 = 左对齐，1 = 居中，2 = 右对齐
    /// 由 TTML 的 role 属性或 AMLL 的 role 字段决定
    /// </summary>
    public int Alignment { get; set; } = 1; // 默认居中
}

/// <summary>
/// 逐字时间戳（用于卡拉OK效果，预留）
/// </summary>
public class WordTimestamp
{
    public string Word { get; set; } = string.Empty;
    public TimeSpan Start { get; set; }
    public TimeSpan Duration { get; set; }
}
