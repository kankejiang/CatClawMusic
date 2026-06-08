using AndroidX.Media3.Common.Audio;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Services;

/// <summary>
/// TeeAudioProcessor: 从 ExoPlayer 音频管道无损截取 PCM 数据用于可视化，
/// 不修改音频数据（完全透传），无需 RECORD_AUDIO 权限。
///
/// PCM 处理优先使用 C++ 原生库（NEON SIMD 加速），失败时回退到 C# 实现。
/// </summary>
public class TeeAudioProcessor : BaseAudioProcessor
{
    private readonly float[] _tempBuffer = new float[4096];
    private int _tempCount;

    private float[]? _fallbackSpectrumBuffer;

    /// <summary>最新的频谱数据（32 个频带，0~1 归一化值）</summary>
    public float[] LatestSpectrum { get; private set; } = Array.Empty<float>();
    /// <summary>频谱数据更新时触发</summary>
    public event Action<float[]>? SpectrumUpdated;

    /// <summary>队列输入音频数据，透传原始数据并处理 PCM 用于频谱计算</summary>
    public override void QueueInput(Java.Nio.ByteBuffer? inputBuffer)
    {
        if (inputBuffer == null) return;
        var remaining = inputBuffer.Remaining();
        var outputBuffer = ReplaceOutputBuffer(remaining);
        if (outputBuffer != null)
        {
            var pos = inputBuffer.Position();
            ProcessAudioData(inputBuffer, remaining);
            inputBuffer.Position(pos);
            outputBuffer.Put(inputBuffer);
            outputBuffer.Flip();
        }
    }

    private void ProcessAudioData(Java.Nio.ByteBuffer buffer, int size)
    {
        var channelCount = 2;
        var sampleCount = size / 2;
        var monoCount = sampleCount / channelCount;

        if (monoCount == 0) return;

        /* 纯 C# PCM 处理（NativeAOT 编译为原生代码，性能等同 C++） */
        ProcessAudioDataFallback(buffer, size, channelCount, monoCount);
    }

    /// <summary>
    /// C# 回退实现：逐样本从 ByteBuffer 读取 short 并转换
    /// </summary>
    private void ProcessAudioDataFallback(Java.Nio.ByteBuffer buffer, int size, int channelCount, int monoCount)
    {
        var shortBuffer = buffer.AsShortBuffer();
        shortBuffer.Position(0);

        var samples = monoCount > _tempBuffer.Length ? _tempBuffer.Length : monoCount;
        for (int i = 0; i < samples; i++)
            _tempBuffer[i] = Math.Abs((float)shortBuffer.Get(i * channelCount) / 32768f);

        _tempCount = samples;
        ComputeSpectrumBands();
    }

    private void ComputeSpectrumBands()
    {
        const int bands = 32;

        /* 纯 C# 频谱条带计算（NativeAOT 编译为原生代码） */
        if (_fallbackSpectrumBuffer == null || _fallbackSpectrumBuffer.Length < bands)
            _fallbackSpectrumBuffer = new float[bands];
        var spectrum = _fallbackSpectrumBuffer;
        var samplesPerBand = Math.Max(1, _tempCount / bands);

        for (int b = 0; b < bands; b++)
        {
            float sum = 0;
            for (int s = b * samplesPerBand; s < (b + 1) * samplesPerBand && s < _tempCount; s++)
                sum += _tempBuffer[s];
            spectrum[b] = Math.Min(1f, sum / samplesPerBand * 3.5f);
        }

        LatestSpectrum = spectrum;
        SpectrumUpdated?.Invoke(spectrum);
    }

    public override bool IsActive => true;

    [System.Obsolete]
    protected override void OnFlush()
    {
        _tempCount = 0;
    }
}
