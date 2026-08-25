using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// EBU R128 响度分析服务（宿主实现，供插件批量 ReplayGain 使用）。
/// 底层通常复用 FFmpeg 的 <c>ebur128=peak=true</c> 滤镜，解析出整体响度与真实峰值，
/// 换算为 ReplayGain 增益。SAF content:// URI 由实现负责落到临时文件后分析。
/// </summary>
public interface ILoudnessAnalyzer
{
    /// <summary>分析单个音频文件的响度，返回 ReplayGain 参数；失败或不可用返回 null</summary>
    /// <param name="uri">音频文件 URI（content:// 或本地绝对路径）</param>
    Task<LoudnessResult?> AnalyzeAsync(string uri, CancellationToken ct = default);
}