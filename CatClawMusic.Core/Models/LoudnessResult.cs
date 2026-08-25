namespace CatClawMusic.Core.Models;

/// <summary>
/// EBU R128 响度分析结果（供 ReplayGain 批量写入使用）。
/// <para>IntegratedLufs 为整体响度（负数，如 -13.8）；TrackGainDb 为相对 89 dB / -18 LUFS 参考的增益。</para>
/// </summary>
public class LoudnessResult
{
    /// <summary>整体响度（LUFS，负值）</summary>
    public double IntegratedLufs { get; set; }

    /// <summary>相对 -18 LUFS 参考的 ReplayGain 增益（dB）</summary>
    public double TrackGainDb { get; set; }

    /// <summary>真实峰值（线性，0..~1.3）</summary>
    public double TrackPeak { get; set; }

    /// <summary>写成 REPLAYGAIN_TRACK_GAIN 帧的文本，如 "+0.10 dB"</summary>
    public string TrackGainTag
        => TrackGainDb.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + " dB";

    /// <summary>写成 REPLAYGAIN_TRACK_PEAK 帧的文本，如 "0.998630"</summary>
    public string TrackPeakTag
        => TrackPeak.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
}