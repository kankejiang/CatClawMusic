using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 10段软件均衡器：基于 Biquad peaking 滤波器的实时音频 EQ 处理器。
/// 频率: 31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz
/// 增益: -15dB ~ +15dB，0.1dB 步长
/// 处理单声道/立体声 float 样本。
/// </summary>
public class EqBandProcessor
{
    public const int Bands = 10;

    /// <summary>10段中心频率 (Hz)</summary>
    public static readonly int[] Freqs = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private float _sampleRate = 44100;
    private readonly float[] _gains = new float[Bands]; // 0~1 (map from -15..+15 dB)
    private bool _enabled;

    // Biquad 状态
    private readonly float[] _b0 = new float[Bands];
    private readonly float[] _b1 = new float[Bands];
    private readonly float[] _b2 = new float[Bands];
    private readonly float[] _a1 = new float[Bands];
    private readonly float[] _a2 = new float[Bands];
    private readonly float[][] _z1; // [channels * bands]
    private readonly float[][] _z2;
    private int _channels;
    private float _q = 1.0f; // Q factor for bandwidth

    public EqBandProcessor(int channels = 2, float sampleRate = 44100)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _z1 = new float[channels][];
        _z2 = new float[channels][];
        for (int c = 0; c < channels; c++)
        {
            _z1[c] = new float[Bands];
            _z2[c] = new float[Bands];
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>设置采样率</summary>
    public void SetSampleRate(float sr)
    {
        if (Math.Abs(sr - _sampleRate) < 0.1f) return;
        _sampleRate = sr;
        RecomputeAllCoefficients();
    }

    /// <summary>设置频段增益，level: -1500 ~ +1500 (millibels)，即 -15.0dB ~ +15.0dB</summary>
    public void SetBandGain(int band, short levelMillibels)
    {
        if (band < 0 || band >= Bands) return;
        float db = levelMillibels / 100f;
        float gain = Math.Max(0f, Math.Min(1f, (db + 15f) / 30f));
        if (Math.Abs(_gains[band] - gain) < 0.001f) return;
        _gains[band] = gain;
        RecomputeBandCoefficients(band);
    }

    /// <summary>获取频段当前 dB 增益</summary>
    public float GetBandDb(int band)
    {
        if (band < 0 || band >= Bands) return 0;
        return _gains[band] * 30f - 15f; // map back 0..1 => -15..+15 dB
    }

    /// <summary>通过毫分贝级别设置</summary>
    public void SetBandLevelMillibels(int band, short millibels) => SetBandGain(band, millibels);

    /// <summary>获取频段毫分贝级别</summary>
    public short GetBandLevelMillibels(int band)
    {
        float db = GetBandDb(band);
        return (short)(int)(db * 100f);
    }

    /// <summary>处理单声道样本块</summary>
    public void ProcessChannel(float[] samples, int offset, int count, int channel)
    {
        if (!_enabled || samples == null) return;

        var z1c = _z1[channel];
        var z2c = _z2[channel];

        for (int i = offset; i < offset + count; i++)
        {
            float x = samples[i];
            float y = x;

            for (int b = 0; b < Bands; b++)
            {
                float w = x - _a1[b] * z1c[b] - _a2[b] * z2c[b];
                float outBand = _b0[b] * w + _b1[b] * z1c[b] + _b2[b] * z2c[b];
                z2c[b] = z1c[b];
                z1c[b] = w;
                y = outBand;
                x = y; // cascade to next band
            }

            samples[i] = y;
        }
    }

    /// <summary>处理立体声交错的样本块</summary>
    /// <param name="samples">交错样本: L0,R0,L1,R1,...</param>
    public void ProcessInterleaved(float[] samples, int frameCount)
    {
        if (!_enabled || _channels < 2) return;

        var z1L = _z1[0];
        var z2L = _z2[0];
        var z1R = _z1[1];
        var z2R = _z2[1];

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float xL = samples[idx];
            float yL = ProcessStereoSample(xL, z1L, z2L, ref xL);
            samples[idx] = yL;

            float xR = samples[idx + 1];
            float yR = ProcessStereoSample(xR, z1R, z2R, ref xR);
            samples[idx + 1] = yR;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ProcessStereoSample(float input, float[] z1c, float[] z2c, ref float carry)
    {
        float x = input;
        float y = input;
        for (int b = 0; b < Bands; b++)
        {
            float w = x - _a1[b] * z1c[b] - _a2[b] * z2c[b];
            float outBand = _b0[b] * w + _b1[b] * z1c[b] + _b2[b] * z2c[b];
            z2c[b] = z1c[b];
            z1c[b] = w;
            y = outBand;
            x = y;
        }
        return y;
    }

    #region Biquad Coefficients

    private void RecomputeAllCoefficients()
    {
        for (int b = 0; b < Bands; b++)
            RecomputeBandCoefficients(b);
    }

    /// <summary>
    /// 计算 peaking EQ biquad 系数。
    /// 使用 RBJ Audio EQ Cookbook 公式:
    /// A = 10^(gainDB/40), w0 = 2*pi*f0/fs, alpha = sin(w0)/(2*Q)
    /// b0 = 1 + alpha*A
    /// b1 = -2*cos(w0)
    /// b2 = 1 - alpha*A
    /// a0 = 1 + alpha/A
    /// a1 = -2*cos(w0)
    /// a2 = 1 - alpha/A
    /// 最终归一化: b0/=a0, b1/=a0, b2/=a0, a1/=a0, a2/=a0
    /// </summary>
    private void RecomputeBandCoefficients(int band)
    {
        float gainDb = _gains[band] * 30f - 15f; // -15..+15 dB
        if (Math.Abs(gainDb) < 0.01f)
        {
            // Unity gain → bypass this band
            _b0[band] = 1; _b1[band] = 0; _b2[band] = 0;
            _a1[band] = 0; _a2[band] = 0;
            return;
        }

        float A = (float)Math.Pow(10, gainDb / 40.0);
        float w0 = 2f * (float)Math.PI * Freqs[band] / _sampleRate;
        float cosW0 = (float)Math.Cos(w0);
        float sinW0 = (float)Math.Sin(w0);
        float alpha = sinW0 / (2f * _q);

        float a0 = 1f + alpha / A;
        float b0 = 1f + alpha * A;
        float b1 = -2f * cosW0;
        float b2 = 1f - alpha * A;
        float a1 = -2f * cosW0;
        float a2 = 1f - alpha / A;

        // Normalize
        float invA0 = 1f / a0;
        _b0[band] = b0 * invA0;
        _b1[band] = b1 * invA0;
        _b2[band] = b2 * invA0;
        _a1[band] = a1 * invA0;
        _a2[band] = a2 * invA0;
    }

    #endregion
}