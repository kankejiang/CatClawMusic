using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using Xunit;

namespace CatClawMusic.Core.Tests;

/// <summary>
/// LRC 解析器语义回归测试（本地音乐播放器标准语义）：
/// - 标准行解析、元数据标签、无时间戳行忽略
/// - 行内不做"原文/译文"猜测拆分（署名行如 "作词 aimer" 保持完整单行）
/// - 同时间戳双行译文配对（含毫秒容差）
/// - 多时间戳逐字行 → WordTimestamps
/// </summary>
public class SplitBilingualTests
{
    private static LrcLyrics? Parse(string lrc) => new LyricsService().ParseLrc(lrc);

    [Fact]
    public void StandardLine_ParsesText()
    {
        var parsed = Parse("[00:12.50]第一句歌词\n[00:17.20]第二句歌词\n");
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Lines.Count);
        Assert.Equal("第一句歌词", parsed.Lines[0].Text);
        Assert.Equal(new TimeSpan(0, 0, 0, 12, 500), parsed.Lines[0].Timestamp);
    }

    [Fact]
    public void MetadataTags_ParsedToMetadata()
    {
        var parsed = Parse("[ti:起风了]\n[ar:买辣椒也用券]\n[00:12.50]歌词\n");
        Assert.NotNull(parsed);
        Assert.Equal("起风了", parsed.Metadata.Title);
        Assert.Equal("买辣椒也用券", parsed.Metadata.Artist);
    }

    [Fact]
    public void NoTimestampLine_Ignored()
    {
        // 无时间戳的署名行/杂项行不显示（标准 LRC 语义）
        var parsed = Parse("作词：aimer\n作曲：泽野弘之\n[00:12.50]歌词\n");
        Assert.NotNull(parsed);
        Assert.Single(parsed.Lines);
        Assert.Equal("歌词", parsed.Lines[0].Text);
    }

    [Fact]
    public void CreditsLine_NotSplitInLine()
    {
        // 行内不做原文/译文猜测拆分："作词 aimer" 保持完整单行
        var parsed = Parse("[00:00.00]作词 aimer\n");
        Assert.NotNull(parsed);
        Assert.Equal("作词 aimer", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }

    [Fact]
    public void MixedScriptLine_NotSplitInLine()
    {
        // "你好 Hello"、"君の名は / 你的名字" 等行内混排不再猜测拆分，原文完整保留
        var parsed = Parse("[00:00.00]君の名は / 你的名字\n");
        Assert.NotNull(parsed);
        Assert.Equal("君の名は / 你的名字", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }

    [Fact]
    public void SameTimestampTranslation_Paired()
    {
        // 同时间戳双行：第一行原文，第二行译文 → Translation 字段（译文行被吸收，剩一行）
        var parsed = Parse("[00:12.50]风起之时\n[00:12.50]When the wind rises\n");
        Assert.NotNull(parsed);
        Assert.Single(parsed.Lines);
        Assert.Equal("风起之时", parsed.Lines[0].Text);
        Assert.Equal("When the wind rises", parsed.Lines[0].Translation);
    }

    [Fact]
    public void NearTimestampTranslation_PairedWithTolerance()
    {
        // 译文时间戳与原文差几毫秒（300ms 容差内）也能配对
        var parsed = Parse("[00:12.500]风起之时\n[00:12.640]When the wind rises\n");
        Assert.NotNull(parsed);
        Assert.Single(parsed.Lines);
        Assert.Equal("风起之时", parsed.Lines[0].Text);
        Assert.Equal("When the wind rises", parsed.Lines[0].Translation);
    }

    [Fact]
    public void WordTimestampLine_KeepsWordTimestamps()
    {
        // 网易云逐字格式：[00:00.000]起[00:00.211]风[00:00.422]了
        var parsed = Parse("[00:00.000]起[00:00.211]风[00:00.422]了\n");
        Assert.NotNull(parsed);
        Assert.Single(parsed.Lines);
        Assert.Equal("起风了", parsed.Lines[0].Text);
        Assert.NotNull(parsed.Lines[0].WordTimestamps);
        Assert.Equal(3, parsed.Lines[0].WordTimestamps.Count);
    }

    [Fact]
    public void MultipleTimestampsOneLine_ExpandsRows()
    {
        // 一行多时间戳 [01:00][02:00]重复句 → 展开为两行
        var parsed = Parse("[00:10.00][00:20.00]重复的歌词\n");
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Lines.Count);
        Assert.All(parsed.Lines, l => Assert.Equal("重复的歌词", l.Text));
    }

    [Fact]
    public void PureInstrumental_MarkedAsEmpty()
    {
        var parsed = Parse("[00:00.00]纯音乐，请欣赏\n");
        Assert.NotNull(parsed);
        Assert.Single(parsed.Lines);
        Assert.Equal("", parsed.Lines[0].Text);
    }
}
