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
    /// <summary>该行歌词的开始时间</summary>
    public TimeSpan Timestamp { get; set; }

    /// <summary>歌词文本</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>翻译文本（可选）</summary>
    public string? Translation { get; set; }

    /// <summary>逐字时间戳列表，用于卡拉OK效果（可选）</summary>
    public List<WordTimestamp>? WordTimestamps { get; set; }
    
    /// <summary>
    /// 歌词行对齐方式（用于合唱/对唱布局）
    /// 0 = 左对齐，1 = 居中，2 = 右对齐
    /// 由 TTML 的 role 属性或 AMLL 的 role 字段决定
    /// </summary>
    public int Alignment { get; set; } = 1; // 默认居中

    /// <summary>
    /// 对唱歌词的对方文本（用于同拍左右分栏显示）
    /// 例如 v1 唱 "你说你无法释怀"，v2 唱 "贝壳里隐藏什么期待"，
    /// 合并后 Primary 存 v1 文本，SecondaryText 存 v2 文本。
    /// </summary>
    public string? SecondaryText { get; set; }

    /// <summary>
    /// 对唱歌词的对方对齐方式（与 SecondaryText 对应）
    /// 0 = 左对齐，1 = 居中，2 = 右对齐
    /// </summary>
    public int SecondaryAlignment { get; set; } = 1;

    /// <summary>
    /// 是否为和声/背景人声（如 TTML 中 v1000、v2000 等角色）
    /// UI 层可据此使用更小字号或更低透明度。
    /// </summary>
    public bool IsBackingVocal { get; set; }

    /// <summary>
    /// 原始角色标识（如 TTML 的 ttm:agent="v1"、AMLL 的 singer="希林娜依高"）
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// 歌手/角色名称，用于按歌手聚焦或按性别分栏显示
    /// </summary>
    public string? SingerName { get; set; }
}

/// <summary>
/// 逐字时间戳（用于卡拉OK效果，预留）
/// </summary>
public class WordTimestamp
{
    /// <summary>该字词的文本</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>该字词的开始时间（绝对时间戳）</summary>
    public TimeSpan Start { get; set; }

    /// <summary>该字词的持续时长</summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 逐字歌词填充进度计算工具（播放页面、全屏歌词、桌面歌词共用）。
/// </summary>
public static class LyricFillCalculator
{
    /// <summary>
    /// 计算指定歌词行在给定播放位置的逐字填充进度（0~1）。
    /// 逐行模式返回 1.0；逐字模式按音节时间戳精确加权或线性填充。
    /// </summary>
    /// <param name="line">当前歌词行</param>
    /// <param name="lineIndex">当前行索引</param>
    /// <param name="allLines">全部歌词行</param>
    /// <param name="position">当前播放位置</param>
    /// <param name="lineMode">是否逐行模式</param>
    public static double ComputeFillProgress(
        LrcLyricLine line, int lineIndex, IReadOnlyList<LrcLyricLine> allLines,
        TimeSpan position, bool lineMode)
    {
        if (lineMode)
            return 1.0;

        var lineStart = line.Timestamp;
        var lineEnd = lineIndex + 1 < allLines.Count
            ? allLines[lineIndex + 1].Timestamp
            : lineStart + TimeSpan.FromSeconds(5);

        if (position <= lineStart)
            return 0;
        if (position >= lineEnd)
            return 1.0;

        if (line.WordTimestamps != null && line.WordTimestamps.Count > 0)
            return CalculateSyllableProgress(line.WordTimestamps, position);

        // 标准 LRC：按时间线性填充
        var totalMs = (lineEnd - lineStart).TotalMilliseconds;
        var elapsedMs = (position - lineStart).TotalMilliseconds;
        return totalMs > 0 ? Math.Clamp(elapsedMs / totalMs, 0.0, 1.0) : 1.0;
    }

    /// <summary>
    /// 按音节时间戳计算填充进度：已唱完的音节贡献其字符比例，当前音节按时间进度部分贡献。
    /// WordTimestamp.Start 为绝对时间戳。
    /// </summary>
    public static double CalculateSyllableProgress(
        IReadOnlyList<WordTimestamp> syllables, TimeSpan position)
    {
        var totalChars = 0;
        foreach (var syl in syllables)
            totalChars += syl.Word?.Length ?? 0;
        if (totalChars == 0) return 0;

        double filledChars = 0;
        foreach (var syl in syllables)
        {
            if (string.IsNullOrEmpty(syl.Word)) continue;

            var sylStart = syl.Start;
            var sylDur = syl.Duration;
            if (sylDur <= TimeSpan.Zero)
                sylDur = TimeSpan.FromMilliseconds(300);
            var sylEnd = sylStart + sylDur;

            if (position >= sylEnd)
            {
                filledChars += syl.Word.Length;
            }
            else if (position > sylStart)
            {
                var sylProgress = (position - sylStart).TotalMilliseconds / sylDur.TotalMilliseconds;
                sylProgress = Math.Clamp(sylProgress, 0.0, 1.0);
                filledChars += syl.Word.Length * sylProgress;
            }
        }

        return Math.Clamp(filledChars / totalChars, 0.0, 1.0);
    }
}
