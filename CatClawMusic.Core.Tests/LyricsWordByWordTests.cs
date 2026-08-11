using CatClawMusic.Core.Services;
using Xunit;

namespace CatClawMusic.Core.Tests;

/// <summary>
/// 逐字方括号歌词格式（[00:00.000]起[00:00.211]风[00:00.422]了...）解析回归测试。
/// 早期 ParseLrc 把每个时间戳拆成独立行且文本恒为空（整首歌解析成 N 个空行），
/// 导致安卓/Windows 歌词区域空白。此处用真实《起风了》逐字 LRC 片段验证修复。
/// </summary>
public class LyricsWordByWordTests
{
    private const string WordByWordLrc = """
        [00:00.000]起[00:00.211]风[00:00.422]了[00:00.633] [00:00.844]([00:01.055]旧[00:01.266]版[00:01.477])[00:01.688] [00:01.899]-[00:02.110] [00:02.321]买[00:02.532]辣[00:02.743]椒[00:02.954]也[00:03.165]用[00:03.376]券[00:03.600]
        [00:03.600]词[00:04.502]：[00:05.404]米[00:06.306]果[00:07.210]
        [00:07.210]曲[00:07.930]：[00:08.650]高[00:09.370]桥[00:10.090]优[00:10.810]
        [00:28.887]这[00:29.370]一[00:29.615]路[00:30.119]上[00:30.399]走[00:30.774]走[00:31.123]停[00:31.501]停[00:31.753]
        [00:32.021]顺[00:32.379]着[00:32.695]少[00:33.166]年[00:33.515]漂[00:33.791]流[00:33.943]的[00:34.114]痕[00:34.413]迹[00:34.690]
        [00:35.068]迈[00:35.513]出[00:35.842]车[00:36.279]站[00:36.592]的[00:36.896]前[00:37.251]一[00:37.667]刻[00:37.980]
        """;

    [Fact]
    public void WordByWordLrc_ParsesToWholeLines_NotPerCharEmptyLines()
    {
        var svc = new LyricsService();
        var parsed = svc.ParseLrc(WordByWordLrc);

        Assert.NotNull(parsed);
        // 6 行输入 → 必须是 6 行歌词（不能拆成每字一行、也不能全空）
        Assert.Equal(6, parsed.Lines.Count);

        // 首行必须完整可读，且带逐字时间戳
        var first = parsed.Lines[0];
        Assert.Equal("起风了 (旧版) - 买辣椒也用券", first.Text);
        Assert.NotNull(first.WordTimestamps);
        Assert.Equal(TimeSpan.Zero, first.Timestamp);
        // 18 个时间戳中行尾锚点 [00:03.600] 无文本被跳过 → 17 个字词
        Assert.Equal(17, first.WordTimestamps.Count);
        Assert.Equal("起", first.WordTimestamps[0].Word);
        Assert.Equal(TimeSpan.Zero, first.WordTimestamps[0].Start);
        Assert.Equal("券", first.WordTimestamps[^1].Word);
        // 行尾锚点时间戳 [00:03.600] 无文本，不应生成空 word
        Assert.All(first.WordTimestamps, w => Assert.False(string.IsNullOrEmpty(w.Word)));
    }

    [Fact]
    public void WordByWordLrc_TimestampAndDuration_AreConsistent()
    {
        var svc = new LyricsService();
        var parsed = svc.ParseLrc(WordByWordLrc);

        // 第二行：词：米果（首个时间戳 03.600 与上行行尾锚点重叠，是下一行真正起点）
        var line2 = parsed.Lines[1];
        Assert.Equal("词：米果", line2.Text);
        Assert.Equal(TimeSpan.FromMilliseconds(3600), line2.Timestamp);

        // 每个 word 的 Duration = 下一时间戳 - 当前时间戳（逐字填充依据）
        var words = line2.WordTimestamps!;
        Assert.Equal(TimeSpan.FromMilliseconds(902), words[0].Duration); // 03.600 → 04.502
        Assert.Equal(TimeSpan.FromMilliseconds(902), words[1].Duration); // 04.502 → 05.404
    }

    [Fact]
    public void StandardLrc_StillParsesNormally()
    {
        var svc = new LyricsService();
        const string standardLrc = """
            [ti:测试]
            [ar:歌手]
            [00:12.34]第一句歌词
            [00:15.00][00:20.00]重复时间戳行
            [00:18.00]第二句歌词
            """;

        var parsed = svc.ParseLrc(standardLrc);
        Assert.NotNull(parsed);
        Assert.Equal("测试", parsed.Metadata.Title);
        // 3 个带时间戳的行，其中重复时间戳行 [00:15][00:20] 生成 2 行，排序后共 4 行
        Assert.Equal(4, parsed.Lines.Count);
        Assert.Equal("第一句歌词", parsed.Lines[0].Text);
        Assert.Equal("重复时间戳行", parsed.Lines[1].Text);
        Assert.Equal("第二句歌词", parsed.Lines[2].Text);
        Assert.Equal("重复时间戳行", parsed.Lines[3].Text);
        // 标准 LRC 不产生 WordTimestamps（无逐字信息）
        Assert.Null(parsed.Lines[0].WordTimestamps);
    }
}
