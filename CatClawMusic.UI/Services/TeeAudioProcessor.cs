using AndroidX.Media3.Common.Audio;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Services;

/// <summary>
/// TeeAudioProcessor: 从 ExoPlayer 音频管道截取 PCM 数据用于可视化和软件 EQ。
/// EQ 处理时修改音频数据，否则完全透传。无需 RECORD_AUDIO 权限。
/// </summary>
public class TeeAudioProcessor : BaseAudioProcessor
{
    private readonly float[] _tempBuffer = new float[4096];
    private int _tempCount;

    private float[]? _fallbackSpectrumBuffer;
    private float[]? _eqBuffer;
    private float[]? _eqInterleavedBuffer;

    /// <summary>软件 10 段均衡器（FFmpeg anequalizer 算法，可为 null）</summary>
    public EqBandProcessor? EqProcessor { get; set; }

    /// <summary>最新的频谱数据（32 个频带，0~1 归一化值）</summary>
    public float[] LatestSpectrum { get; private set; } = Array.Empty<float>();
    /// <summary>频谱数据更新时触发</summary>
    public event Action<float[]>? SpectrumUpdated;

    /// <summary>采样率（由 ExoPlayer 设置）</summary>
    public float CurrentSampleRate { get; set; } = 44100;

    /// <summary>队列输入音频数据，处理可视化 + EQ</summary>
    public override void QueueInput(Java.Nio.ByteBuffer? inputBuffer)
    {
        if (inputBuffer == null) return;
        var remaining = inputBuffer.Remaining();
        var outputBuffer = ReplaceOutputBuffer(remaining);
        if (outputBuffer != null)
        {
            var pos = inputBuffer.Position();
            var shortBuf = inputBuffer.AsShortBuffer();
            shortBuf.Position(0);

            int sampleCount = remaining / 2;
            int frameCount = sampleCount / 2;

            // 频谱计算：从左声道提取幅度
            var monoCount = Math.Min(frameCount, _tempBuffer.Length);
            for (int i = 0; i < monoCount; i++)
                _tempBuffer[i] = Math.Abs((float)shortBuf.Get(i * 2) / 32768f);
            _tempCount = monoCount;
            ComputeSpectrumBands();

            // EQ 处理：读写 short 样本到 output
            var eq = EqProcessor;
            if (eq != null && eq.Enabled)
            {
                ApplyEq(shortBuf, sampleCount, frameCount, eq);
                inputBuffer.Position(pos);
                for (int i = 0; i < sampleCount; i++)
                    outputBuffer.PutShort(shortBuf.Get(i));
            }
            else
            {
                inputBuffer.Position(pos);
                outputBuffer.Put(inputBuffer);
            }
            outputBuffer.Flip();
        }
    }

    private void ApplyEq(Java.Nio.ShortBuffer shortBuf, int sampleCount, int frameCount, EqBandProcessor eq)
    {
        eq.SetSampleRate(CurrentSampleRate);

        if (_eqInterleavedBuffer == null || _eqInterleavedBuffer.Length < sampleCount)
            _eqInterleavedBuffer = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
            _eqInterleavedBuffer[i] = (float)shortBuf.Get(i) / 32768f;

        eq.ProcessInterleaved(_eqInterleavedBuffer, frameCount);

        for (int i = 0; i < sampleCount; i++)
        {
            float v = _eqInterleavedBuffer[i];
            if (v > 1f) v = 1f;
            if (v < -1f) v = -1f;
            shortBuf.Put(i, (short)(v * 32767f));
        }
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
