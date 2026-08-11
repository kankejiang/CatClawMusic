using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using Xunit;

namespace CatClawMusic.Core.Tests;

/// <summary>
/// 双语歌词行拆分（SplitBilingual）回归测试：
/// 覆盖显式分隔符（/ ｜ - 等）与空格两种方向的中英/中日/日韩拆分，
/// 以及数字、同脚本连字符、纯中文等不应误拆的场景。
/// </summary>
public class SplitBilingualTests
{
    private static LrcLyrics? Parse(string lrc) => new LyricsService().ParseLrc(lrc);

    [Fact]
    public void SlashSeparator_SplitsJapaneseChinese()
    {
        var parsed = Parse("[00:00.00]君の名は / 你的名字\n");
        Assert.NotNull(parsed);
        Assert.Equal("君の名は", parsed.Lines[0].Text);
        Assert.Equal("你的名字", parsed.Lines[0].Translation);
    }

    [Fact]
    public void SlashSeparator_SplitsEnglishChinese()
    {
        var parsed = Parse("[00:00.00]Love you / 我爱你\n");
        Assert.NotNull(parsed);
        Assert.Equal("Love you", parsed.Lines[0].Text);
        Assert.Equal("我爱你", parsed.Lines[0].Translation);
    }

    [Fact]
    public void FullWidthPipe_SplitsJapaneseChinese()
    {
        var parsed = Parse("[00:00.00]君の名は｜你的名字\n");
        Assert.NotNull(parsed);
        Assert.Equal("君の名は", parsed.Lines[0].Text);
        Assert.Equal("你的名字", parsed.Lines[0].Translation);
    }

    [Fact]
    public void DashSeparator_SplitsChineseEnglish()
    {
        var parsed = Parse("[00:00.00]起风了 - Qi Feng Liao\n");
        Assert.NotNull(parsed);
        Assert.Equal("起风了", parsed.Lines[0].Text);
        Assert.Equal("Qi Feng Liao", parsed.Lines[0].Translation);
    }

    [Fact]
    public void SpaceSeparator_CjkThenLatin_Splits()
    {
        // 中文原文 + 空格 + 英文译文（原策略只支持英文在前的方向）
        var parsed = Parse("[00:00.00]你好 Hello\n");
        Assert.NotNull(parsed);
        Assert.Equal("你好", parsed.Lines[0].Text);
        Assert.Equal("Hello", parsed.Lines[0].Translation);
    }

    [Fact]
    public void SpaceSeparator_LatinThenCjk_Splits()
    {
        // 英文原文 + 空格 + 中文译文（回归：原策略2 已支持的方向）
        var parsed = Parse("[00:00.00]Hello 你好\n");
        Assert.NotNull(parsed);
        Assert.Equal("Hello", parsed.Lines[0].Text);
        Assert.Equal("你好", parsed.Lines[0].Translation);
    }

    [Fact]
    public void SameScriptSeparator_NotSplit()
    {
        // 两侧同为中文：不拆（无法可靠区分原文/译文）
        var parsed = Parse("[00:00.00]一途 / 一途\n");
        Assert.NotNull(parsed);
        Assert.Equal("一途 / 一途", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }

    [Fact]
    public void DigitFraction_NotSplit()
    {
        var parsed = Parse("[00:00.00]1/2\n");
        Assert.NotNull(parsed);
        Assert.Equal("1/2", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }

    [Fact]
    public void LatinHyphenWord_NotSplit()
    {
        var parsed = Parse("[00:00.00]a-ha\n");
        Assert.NotNull(parsed);
        Assert.Equal("a-ha", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }

    [Fact]
    public void PureChineseLine_NotSplit()
    {
        var parsed = Parse("[00:00.00]第一句歌词\n");
        Assert.NotNull(parsed);
        Assert.Equal("第一句歌词", parsed.Lines[0].Text);
        Assert.Null(parsed.Lines[0].Translation);
    }
}
