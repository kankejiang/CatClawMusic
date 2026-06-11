using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 10段软件均衡器：基于 FFmpeg anequalizer 同级算法的实时音频 EQ 处理器。
/// 使用 double 精度计算 Biquad 滤波器系数（与 FFmpeg libavfilter 一致），
/// 处理阶段使用 float 以保证实时性能。
/// 
/// 频率: 31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz（ISO 标准倍频程）
/// 增益: -15dB ~ +15dB
/// Q: √2 ≈ 1.4142（Butterworth 响应，每段覆盖约 1 倍频程）
/// 
/// 算法等价于 FFmpeg 命令:
///   ffmpeg -i input.wav -af "anequalizer=bands='f=31 w=1.4142 g=0 t=q|...'" output.wav
/// </summary>
public class EqBandProcessor
{
    public const int Bands = 10;

    /// <summary>10段中心频率 (Hz) — ISO 标准倍频程间隔</summary>
    public static readonly int[] Freqs = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private double _sampleRate = 44100;
    private readonly double[] _gains = new double[Bands]; // 0~1 (map from -15..+15 dB)
    private bool _enabled;

    // Biquad 系数 — double 精度计算，float 存储（与 FFmpeg anequalizer 一致）
    private readonly float[] _b0 = new float[Bands];
    private readonly float[] _b1 = new float[Bands];
    private readonly float[] _b2 = new float[Bands];
    private readonly float[] _a1 = new float[Bands];
    private readonly float[] _a2 = new float[Bands];

    // Biquad 延迟状态（TDF-II 结构，与 FFmpeg biquad 实现一致）
    private readonly float[][] _z1; // [channels][bands]
    private readonly float[][] _z2;
    private int _channels;

    // Q factor — √2 (Butterworth) 让每个频段覆盖约 1 倍频程，段间干扰最小
    private const double Q = 1.4142135623730951; // Math.Sqrt(2)

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
        // 初始化增益为 0.5（映射为 0dB / 无增益变化）
        // 映射关系: gain 0→-15dB, 0.5→0dB, 1.0→+15dB
        for (int b = 0; b < Bands; b++)
            _gains[b] = 0.5;
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
        double db = levelMillibels / 100.0;
        double gain = Math.Max(0.0, Math.Min(1.0, (db + 15.0) / 30.0));
        if (Math.Abs(_gains[band] - gain) < 0.0001) return;
        _gains[band] = gain;
        RecomputeBandCoefficients(band);
    }

    /// <summary>获取频段当前 dB 增益</summary>
    public float GetBandDb(int band)
    {
        if (band < 0 || band >= Bands) return 0;
        return (float)(_gains[band] * 30.0 - 15.0); // map back 0..1 => -15..+15 dB
    }

    /// <summary>通过毫分贝级别设置</summary>
    public void SetBandLevelMillibels(int band, short millibels) => SetBandGain(band, millibels);

    /// <summary>获取频段毫分贝级别</summary>
    public short GetBandLevelMillibels(int band)
    {
        double db = _gains[band] * 30.0 - 15.0;
        return (short)(int)(db * 100.0);
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

    #region Biquad Coefficients (FFmpeg anequalizer algorithm)

    private void RecomputeAllCoefficients()
    {
        for (int b = 0; b < Bands; b++)
            RecomputeBandCoefficients(b);
    }

    /// <summary>
    /// 计算 peaking EQ biquad 系数 — 使用 double 精度（与 FFmpeg anequalizer 一致）。
    /// 
    /// 算法等价于 FFmpeg libavfilter/af_anequalizer.c 中的 coeff() 函数:
    ///   A = 10^(gainDB/40)
    ///   w0 = 2*pi*f0/fs
    ///   alpha = sin(w0)/(2*Q)
    ///   b0 = 1 + alpha*A
    ///   b1 = -2*cos(w0)
    ///   b2 = 1 - alpha*A
    ///   a0 = 1 + alpha/A
    ///   a1 = -2*cos(w0)
    ///   a2 = 1 - alpha/A
    ///   归一化: b0/=a0, b1/=a0, b2/=a0, a1/=a0, a2/=a0
    /// 
    /// 关键改进: 所有中间计算使用 double 精度，避免 float 精度损失导致的
    /// 多频段级联累积误差（这是 10 段 EQ 在 float 精度下不稳定的主因）。
    /// </summary>
    private void RecomputeBandCoefficients(int band)
    {
        double gainDb = _gains[band] * 30.0 - 15.0; // -15..+15 dB
        if (Math.Abs(gainDb) < 0.01)
        {
            // Unity gain → bypass this band
            _b0[band] = 1; _b1[band] = 0; _b2[band] = 0;
            _a1[band] = 0; _a2[band] = 0;
            return;
        }

        // === double 精度计算（FFmpeg anequalizer 同款） ===
        double A = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * Math.PI * Freqs[band] / _sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2.0 * Q);

        double a0 = 1.0 + alpha / A;
        double b0 = 1.0 + alpha * A;
        double b1 = -2.0 * cosW0;
        double b2 = 1.0 - alpha * A;
        double a1 = -2.0 * cosW0;
        double a2 = 1.0 - alpha / A;

        // 归一化 — 在 double 精度下完成后转为 float
        double invA0 = 1.0 / a0;

        _b0[band] = (float)(b0 * invA0);
        _b1[band] = (float)(b1 * invA0);
        _b2[band] = (float)(b2 * invA0);
        _a1[band] = (float)(a1 * invA0);
        _a2[band] = (float)(a2 * invA0);
    }

    #endregion
}
