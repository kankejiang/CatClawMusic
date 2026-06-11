namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 软件音频效果处理器接口。
/// 所有 DSP 效果实现此接口，由 AudioEffectChain 按 Priority 顺序调用。
/// </summary>
public interface IAudioEffect
{
    /// <summary>启用/旁通</summary>
    bool Enabled { get; set; }

    /// <summary>
    /// 就地处理交错立体声 float 样本（L0,R0,L1,R1,...），范围 [-1..+1]。
    /// </summary>
    /// <param name="samples">交错立体声样本缓冲区</param>
    /// <param name="frameCount">帧数（= samples.Length / 2）</param>
    void Process(float[] samples, int frameCount);

    /// <summary>采样率变化时调用（切歌等场景）</summary>
    void SetSampleRate(float sampleRate);

    /// <summary>停止/开始播放时刷新内部状态（延迟线、包络等）</summary>
    void Reset();

    /// <summary>
    /// 处理链中的优先级，数值越小越先执行。
    /// 建议: EQ=0, Compressor=10, Reverb=20, Widener=30, Saturation=40, DeEsser=50, Limiter=60
    /// </summary>
    int Priority { get; }
}
