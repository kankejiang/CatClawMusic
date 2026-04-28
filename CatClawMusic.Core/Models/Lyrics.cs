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
    /// <summary>
    /// 时间戳（TimeSpan）
    /// </summary>
    public TimeSpan Timestamp { get; set; }
    
    /// <summary>
    /// 歌词文本
    /// </summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>
    /// 逐字时间戳（用于卡拉OK效果，预留）
    /// </summary>
    public List<WordTimestamp>? WordTimestamps { get; set; }
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
