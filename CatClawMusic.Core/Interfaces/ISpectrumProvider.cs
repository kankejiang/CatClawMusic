namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 频谱数据提供者（宿主服务契约）：向音频可视化视图广播实时 FFT 频谱数据。
/// </summary>
/// <para>
/// 平台实现在宿主侧（Windows 经 AudioGraph 帧输出回调 / Android 经 Visualizer），
/// Core 只定义契约。可视化插件视图通过 DI 获取本服务并订阅 <see cref="SpectrumUpdated"/>。
/// </para>
/// <para>
/// 注意：本服务是宿主提供给插件的服务（插件消费），不是插件接口（插件提供）。
/// 与 IAudioVisualizerPlugin 配套使用：宿主挂插件视图，插件视图订阅本服务取数据。
/// </para>
/// </summary>
public interface ISpectrumProvider
{
    /// <summary>
    /// 频谱更新事件。参数为归一化频谱柱数组（每个元素 0.0~1.0），按频率从低到高排列。
    /// 播放时高频触发（约 30~60 fps），订阅方应在 UI 线程安全更新。
    /// </summary>
    event Action<float[]>? SpectrumUpdated;

    /// <summary>当前是否有频谱数据流（播放中且有可视化需求）</summary>
    bool IsActive { get; }

    /// <summary>启动频谱采样（播放页 / 可视化视图出现时调用）</summary>
    void Start();

    /// <summary>停止频谱采样（播放页 / 可视化视图消失时调用）</summary>
    void Stop();
}
