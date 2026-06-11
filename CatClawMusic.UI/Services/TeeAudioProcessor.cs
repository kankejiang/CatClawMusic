using AndroidX.Media3.Common.Audio;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Services.Effects;

namespace CatClawMusic.UI.Services;

/// <summary>
/// TeeAudioProcessor: 从 ExoPlayer 音频管道截取 PCM 数据用于可视化和软件音效处理链。
/// 音效处理链由 AudioEffectChain 编排（EQ + 压限器 + 混响 + 其他效果）。
/// 无需 RECORD_AUDIO 权限。
/// </summary>
public class TeeAudioProcessor : BaseAudioProcessor
{
    private readonly float[] _tempBuffer = new float[4096];
    private int _tempCount;

    private float[]? _fallbackSpectrumBuffer;
    private float[]? _processBuffer;

    /// <summary>音效处理链（包含 EQ 和所有软件效果，可为 null）</summary>
    public AudioEffectChain? EffectChain { get; set; }

    /// <summary>最新的频谱数据（32 个频带，0~1 归一化值）</summary>
    public float[] LatestSpectrum { get; private set; } = Array.Empty<float>();
    /// <summary>频谱数据更新时触发</summary>
    public event Action<float[]>? SpectrumUpdated;

    /// <summary>采样率（由 ExoPlayer 设置）</summary>
    public float CurrentSampleRate { get; set; } = 44100;

    /// <summary>队列输入音频数据，处理可视化 + 音效链</summary>
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

            // 频谱计算：从左声道提取幅度（从原始输入读取，不受效果处理影响）
            var monoCount = Math.Min(frameCount, _tempBuffer.Length);
            for (int i = 0; i < monoCount; i++)
                _tempBuffer[i] = Math.Abs((float)shortBuf.Get(i * 2) / 32768f);
            _tempCount = monoCount;
            ComputeSpectrumBands();

            // 音效链处理：EQ + 所有软件效果
            var chain = EffectChain;
            if (chain != null)
            {
                chain.SetSampleRate(CurrentSampleRate);

                if (_processBuffer == null || _processBuffer.Length < sampleCount)
                    _processBuffer = new float[sampleCount];

                // short → float
                for (int i = 0; i < sampleCount; i++)
                    _processBuffer[i] = (float)shortBuf.Get(i) / 32768f;

                // 处理链: EQ → Compressor → Reverb → Widener → Saturation → DeEsser → Limiter
                chain.Process(_processBuffer, frameCount);

                // float → short (带钳位)
                inputBuffer.Position(pos);
                for (int i = 0; i < sampleCount; i++)
                {
                    float v = _processBuffer[i];
                    if (v > 1f) v = 1f;
                    if (v < -1f) v = -1f;
                    outputBuffer.PutShort((short)(v * 32767f));
                }
            }
            else
            {
                inputBuffer.Position(pos);
                outputBuffer.Put(inputBuffer);
            }
            outputBuffer.Flip();
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
