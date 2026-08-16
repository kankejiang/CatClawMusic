using SQLite;

namespace CatClawMusic.Core.Models;

/// <summary>按天汇总的播放统计，用于加速听歌统计页加载。</summary>
[Table("DailyPlayStat")]
public class DailyPlayStat
{
    /// <summary>本地日期 "yyyy-MM-dd"</summary>
    [PrimaryKey]
    public string Date { get; set; } = "";

    /// <summary>当天播放次数</summary>
    public int PlayCount { get; set; }

    /// <summary>当天累计聆听时长（毫秒）</summary>
    public long TotalDurationMs { get; set; }

    /// <summary>当天深夜时段（21:00-05:00）播放次数</summary>
    public int NightPlayCount { get; set; }
}

/// <summary>按小时汇总的播放统计，用于时段分布快速查询。</summary>
[Table("HourlyPlayStat")]
public class HourlyPlayStat
{
    /// <summary>主键 "yyyy-MM-dd HH"（24 小时制）</summary>
    [PrimaryKey]
    public string DateHour { get; set; } = "";

    /// <summary>本地日期 "yyyy-MM-dd"</summary>
    [Indexed]
    public string Date { get; set; } = "";

    /// <summary>小时 0-23</summary>
    public int Hour { get; set; }

    /// <summary>该小时播放次数</summary>
    public int PlayCount { get; set; }

    /// <summary>该小时累计聆听时长（毫秒）</summary>
    public long TotalDurationMs { get; set; }
}

/// <summary>按天+歌曲汇总的播放统计，用于“听过的歌”去重计数快速查询。</summary>
[Table("DailySongStat")]
public class DailySongStat
{
    /// <summary>主键 "yyyy-MM-dd|songId"</summary>
    [PrimaryKey]
    public string DateSong { get; set; } = "";

    /// <summary>本地日期 "yyyy-MM-dd"</summary>
    [Indexed]
    public string Date { get; set; } = "";

    /// <summary>歌曲 ID</summary>
    public int SongId { get; set; }

    /// <summary>该歌曲当天播放次数</summary>
    public int PlayCount { get; set; }

    /// <summary>该歌曲当天累计聆听时长（毫秒）</summary>
    public long TotalDurationMs { get; set; }
}
