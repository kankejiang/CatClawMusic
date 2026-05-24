using AndroidX.Media3.Common.Audio;

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

    /* 原生库用的 short 缓冲区，避免每帧分配 */
    private short[]? _nativeShortBuffer;
    private float[]? _nativeSpectrumBuffer;
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

        /* 优先使用 C++ 原生库处理 PCM 数据（NEON SIMD 加速） */
        if (NativeInterop.IsAvailable)
        {
            try
            {
                ProcessAudioDataNative(buffer, size, channelCount, monoCount);
                return;
            }
            catch { }
        }

        /* C# 回退实现 */
        ProcessAudioDataFallback(buffer, size, channelCount, monoCount);
    }

    /// <summary>
    /// 使用 C++ 原生库处理 PCM 数据
    /// 一次性将 ByteBuffer 中的 short 数据复制到数组，然后调用原生函数
    /// </summary>
    private void ProcessAudioDataNative(Java.Nio.ByteBuffer buffer, int size, int channelCount, int monoCount)
    {
        var shortBuffer = buffer.AsShortBuffer();
        shortBuffer.Position(0);
        var shortCount = size / 2;

        /* 确保 short 缓冲区足够大 */
        if (_nativeShortBuffer == null || _nativeShortBuffer.Length < shortCount)
            _nativeShortBuffer = new short[shortCount];
        shortBuffer.Get(_nativeShortBuffer, 0, shortCount);

        /* 调用原生 PCM → 单声道绝对值浮点转换 */
        var samples = monoCount > _tempBuffer.Length ? _tempBuffer.Length : monoCount;
        _tempCount = NativeInterop.PcmToMonoAbs(_nativeShortBuffer, shortCount, channelCount, _tempBuffer);
        if (_tempCount > samples) _tempCount = samples;

        ComputeSpectrumBands();
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

        /* 优先使用 C++ 原生库计算频谱条带 */
        if (NativeInterop.IsAvailable)
        {
            try
            {
                if (_nativeSpectrumBuffer == null || _nativeSpectrumBuffer.Length < bands)
                    _nativeSpectrumBuffer = new float[bands];
                NativeInterop.ComputeSpectrumBands(_tempBuffer, _tempCount, bands, _nativeSpectrumBuffer, 3.5f);
                LatestSpectrum = _nativeSpectrumBuffer;
                SpectrumUpdated?.Invoke(_nativeSpectrumBuffer);
                return;
            }
            catch { }
        }

        /* C# 回退实现 */
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
