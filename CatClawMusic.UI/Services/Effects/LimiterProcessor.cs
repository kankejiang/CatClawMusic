using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 砖墙限幅器 (Brick-wall Limiter)。
/// 瞬时峰值检测 + 快速启动 + 指数释放，作为音频处理链的最终安全网。
/// </summary>
public class LimiterProcessor : IAudioEffect
{
    public int Priority => 60;
    public bool Enabled { get; set; }

    private float _ceilingDb = -0.3f;   // -6 ~ 0 dB
    private float _releaseMs = 50f;     // 10 ~ 500 ms

    private float _sampleRate = 44100f;
    private float _ceilingLinear;       // 天花板线性值
    private float _releaseCoeff;        // 释放系数
    private float _envelope;            // 峰值包络

    public float CeilingDb
    {
        get => _ceilingDb;
        set
        {
            _ceilingDb = Math.Clamp(value, -6f, 0f);
            _ceilingLinear = MathF.Pow(10f, _ceilingDb / 20f);
        }
    }

    public float ReleaseMs
    {
        get => _releaseMs;
        set
        {
            _releaseMs = Math.Clamp(value, 10f, 500f);
            _releaseCoeff = MathF.Exp(-1f / (_sampleRate * _releaseMs / 1000f));
        }
    }

    public LimiterProcessor()
    {
        _ceilingLinear = MathF.Pow(10f, _ceilingDb / 20f);
        _releaseCoeff = MathF.Exp(-1f / (_sampleRate * _releaseMs / 1000f));
    }

    public void SetSampleRate(float sampleRate)
    {
        if (MathF.Abs(sampleRate - _sampleRate) < 0.1f) return;
        _sampleRate = sampleRate;
        _releaseCoeff = MathF.Exp(-1f / (_sampleRate * _releaseMs / 1000f));
    }

    public void Reset()
    {
        _envelope = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        float ceiling = _ceilingLinear;
        float release = _releaseCoeff;
        // 快速攻击系数（~2 样本）
        float attack = MathF.Exp(-1f / 2f);

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float L = samples[idx];
            float R = samples[idx + 1];

            // 立体声峰值检测
            float peak = MathF.Max(MathF.Abs(L), MathF.Abs(R));

            // 包络跟随（快攻慢释）
            float coeff = peak > _envelope ? attack : release;
            _envelope = coeff * _envelope + (1f - coeff) * peak;

            // 如果包络超过天花板，计算增益衰减
            if (_envelope > ceiling && _envelope > 1e-10f)
            {
                float gain = ceiling / _envelope;
                samples[idx] = L * gain;
                samples[idx + 1] = R * gain;
            }

            // 硬钳位安全网
            if (samples[idx] > ceiling) samples[idx] = ceiling;
            else if (samples[idx] < -ceiling) samples[idx] = -ceiling;
            if (samples[idx + 1] > ceiling) samples[idx + 1] = ceiling;
            else if (samples[idx + 1] < -ceiling) samples[idx + 1] = -ceiling;
        }
    }
}
