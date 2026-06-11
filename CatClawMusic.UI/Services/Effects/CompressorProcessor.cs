using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 动态范围压缩器 (Compressor)。
/// RMS 电平检测 + 立体声关联 + Attack/Release 弹道学平滑 + Makeup 增益。
/// </summary>
public class CompressorProcessor : IAudioEffect
{
    public int Priority => 10;
    public bool Enabled { get; set; }

    // === 参数 ===
    private float _thresholdDb = -20f;   // -60 ~ 0 dB
    private float _ratio = 4f;           // 1:1 ~ 20:1
    private float _attackMs = 10f;       // 1 ~ 100 ms
    private float _releaseMs = 100f;     // 10 ~ 1000 ms
    private float _makeupDb = 0f;        // 0 ~ +30 dB

    // === 内部状态 ===
    private float _sampleRate = 44100f;
    private float _rmsSquared;           // RMS 电平平方（单极点平滑）
    private float _smoothedGainDb;       // 平滑后的增益衰减 (dB, 负值)
    private float _attackCoeff;          // Attack 系数
    private float _releaseCoeff;         // Release 系数
    private float _makeupLinear = 1f;    // Makeup 增益（线性）

    /// <summary>当前增益衰减 (dB, 负值)，供 UI 表头读取</summary>
    public volatile float CurrentGainReductionDb;

    // === 参数属性 ===

    public float ThresholdDb
    {
        get => _thresholdDb;
        set { _thresholdDb = Math.Clamp(value, -60f, 0f); }
    }

    public float Ratio
    {
        get => _ratio;
        set { _ratio = Math.Clamp(value, 1f, 20f); }
    }

    public float AttackMs
    {
        get => _attackMs;
        set { _attackMs = Math.Clamp(value, 1f, 100f); RecomputeCoefficients(); }
    }

    public float ReleaseMs
    {
        get => _releaseMs;
        set { _releaseMs = Math.Clamp(value, 10f, 1000f); RecomputeCoefficients(); }
    }

    public float MakeupDb
    {
        get => _makeupDb;
        set
        {
            _makeupDb = Math.Clamp(value, 0f, 30f);
            _makeupLinear = MathF.Pow(10f, _makeupDb / 20f);
        }
    }

    public CompressorProcessor()
    {
        RecomputeCoefficients();
    }

    public void SetSampleRate(float sampleRate)
    {
        if (MathF.Abs(sampleRate - _sampleRate) < 0.1f) return;
        _sampleRate = sampleRate;
        RecomputeCoefficients();
    }

    public void Reset()
    {
        _rmsSquared = 0f;
        _smoothedGainDb = 0f;
        CurrentGainReductionDb = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        const float epsilon = 1e-10f;
        float invRatio = 1f / _ratio;
        float gainReductionDb = 0f;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float L = samples[idx];
            float R = samples[idx + 1];

            // 立体声关联: 取峰值
            float peak = MathF.Max(MathF.Abs(L), MathF.Abs(R));

            // RMS 检测（单极点平滑，α 由 attack 时间决定）
            float peakSq = peak * peak;
            float rmsAlpha = 1f - MathF.Exp(-1f / (_sampleRate * 0.005f)); // ~5ms RMS 窗口
            _rmsSquared = rmsAlpha * peakSq + (1f - rmsAlpha) * _rmsSquared;

            // 电平 → dB
            float levelDb = 10f * MathF.Log10(_rmsSquared + epsilon);

            // 增益计算
            float targetReductionDb;
            if (levelDb > _thresholdDb)
            {
                float overDb = levelDb - _thresholdDb;
                targetReductionDb = -overDb * (1f - invRatio);
            }
            else
            {
                targetReductionDb = 0f;
            }

            // Attack/Release 弹道学平滑
            float coeff = targetReductionDb < _smoothedGainDb ? _attackCoeff : _releaseCoeff;
            _smoothedGainDb = coeff * _smoothedGainDb + (1f - coeff) * targetReductionDb;

            gainReductionDb = _smoothedGainDb;

            // dB → 线性增益
            float gainLinear = MathF.Pow(10f, _smoothedGainDb / 20f) * _makeupLinear;

            // 应用增益
            samples[idx] = L * gainLinear;
            samples[idx + 1] = R * gainLinear;
        }

        CurrentGainReductionDb = gainReductionDb;
    }

    private void RecomputeCoefficients()
    {
        float attackS = _attackMs / 1000f;
        float releaseS = _releaseMs / 1000f;
        _attackCoeff = MathF.Exp(-1f / (_sampleRate * attackS));
        _releaseCoeff = MathF.Exp(-1f / (_sampleRate * releaseS));
    }
}
