using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 去齿音处理器 (De-Esser)。
/// 使用二阶 Butterworth 带通滤波器隔离齿音频段，
/// 包络检测驱动全频段平滑增益衰减。
/// </summary>
public class DeEsserProcessor : IAudioEffect
{
    public int Priority => 50;
    public bool Enabled { get; set; }

    private float _frequency = 6000f;   // 2000 ~ 12000 Hz
    private float _sensitivity = 50f;   // 0 ~ 100%
    private float _reductionDb = -10f;  // 0 ~ -20 dB

    private float _sampleRate = 44100f;

    // 带通滤波器系数 (Biquad)
    private float _bpB0, _bpB1, _bpB2, _bpA1, _bpA2;
    // L 通道带通状态
    private float _bpZ1L, _bpZ2L;
    // R 通道带通状态
    private float _bpZ1R, _bpZ2R;

    // 包络检测
    private float _envelope;
    private float _envAttackCoeff;
    private float _envReleaseCoeff;

    // 增益平滑
    private float _smoothGain = 1f;
    private float _gainSmoothCoeff;

    public float Frequency
    {
        get => _frequency;
        set { _frequency = Math.Clamp(value, 2000f, 12000f); RecomputeFilter(); }
    }

    public float Sensitivity
    {
        get => _sensitivity;
        set => _sensitivity = Math.Clamp(value, 0f, 100f);
    }

    public float ReductionDb
    {
        get => _reductionDb;
        set => _reductionDb = Math.Clamp(value, -20f, 0f);
    }

    public DeEsserProcessor()
    {
        RecomputeFilter();
        RecomputeEnvelopeCoeffs();
    }

    public void SetSampleRate(float sampleRate)
    {
        if (MathF.Abs(sampleRate - _sampleRate) < 0.1f) return;
        _sampleRate = sampleRate;
        RecomputeFilter();
        RecomputeEnvelopeCoeffs();
    }

    public void Reset()
    {
        _bpZ1L = _bpZ2L = 0f;
        _bpZ1R = _bpZ2R = 0f;
        _envelope = 0f;
        _smoothGain = 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        // 灵敏度 → 阈值 (dB): sensitivity 0% → -10dB, 100% → -40dB
        float thresholdDb = -10f - (_sensitivity / 100f) * 30f;
        float maxReductionLinear = MathF.Pow(10f, _reductionDb / 20f); // e.g. 0.316 for -10dB

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float L = samples[idx];
            float R = samples[idx + 1];

            // 对 L 通道做带通滤波
            float bpL = _bpB0 * L + _bpB1 * _bpZ1L + _bpB2 * _bpZ2L
                        - _bpA1 * _bpZ1L - _bpA2 * _bpZ2L;
            // 注意: 上面的公式中 _bpA1 和 _bpB1 的项重复了 _bpZ1L，修正为 TDF-II:
            float wL = L - _bpA1 * _bpZ1L - _bpA2 * _bpZ2L;
            bpL = _bpB0 * wL + _bpB1 * _bpZ1L + _bpB2 * _bpZ2L;
            _bpZ2L = _bpZ1L;
            _bpZ1L = wL;

            // 对 R 通道做带通滤波
            float wR = R - _bpA1 * _bpZ1R - _bpA2 * _bpZ2R;
            float bpR = _bpB0 * wR + _bpB1 * _bpZ1R + _bpB2 * _bpZ2R;
            _bpZ2R = _bpZ1R;
            _bpZ1R = wR;

            // 取带通输出的峰值
            float bpPeak = MathF.Max(MathF.Abs(bpL), MathF.Abs(bpR));

            // 包络检测（快攻慢释）
            float coeff = bpPeak > _envelope ? _envAttackCoeff : _envReleaseCoeff;
            _envelope = coeff * _envelope + (1f - coeff) * bpPeak;

            // 包络 → dB
            float envDb = 20f * MathF.Log10(_envelope + 1e-10f);

            // 计算目标增益
            float targetGain = 1f;
            if (envDb > thresholdDb)
            {
                float overDb = envDb - thresholdDb;
                // 映射 overDb 到 [1.0, maxReductionLinear] 范围
                float reduction = MathF.Max(maxReductionLinear, 1f - overDb * 0.1f);
                targetGain = MathF.Max(maxReductionLinear, reduction);
            }

            // 平滑增益
            _smoothGain = _gainSmoothCoeff * _smoothGain + (1f - _gainSmoothCoeff) * targetGain;

            // 应用到原始信号
            samples[idx] = L * _smoothGain;
            samples[idx + 1] = R * _smoothGain;
        }
    }

    private void RecomputeFilter()
    {
        // 二阶 Butterworth 带通滤波器，Q ≈ 2
        float w0 = 2f * MathF.PI * _frequency / _sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float Q = 2f;
        float alpha = sinW0 / (2f * Q);

        float a0 = 1f + alpha;
        _bpB0 = alpha / a0;            // 带通: b0 = alpha
        _bpB1 = 0f;
        _bpB2 = -alpha / a0;
        _bpA1 = -2f * cosW0 / a0;
        _bpA2 = (1f - alpha) / a0;
    }

    private void RecomputeEnvelopeCoeffs()
    {
        // 包络: Attack ~1ms, Release ~50ms
        _envAttackCoeff = MathF.Exp(-1f / (_sampleRate * 0.001f));
        _envReleaseCoeff = MathF.Exp(-1f / (_sampleRate * 0.050f));
        // 增益平滑: ~5ms
        _gainSmoothCoeff = MathF.Exp(-1f / (_sampleRate * 0.005f));
    }
}
