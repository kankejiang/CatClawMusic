using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 磁带饱和 / 暖音处理器。
/// tanh() 软削波产生偶次谐波（模拟磁带温暖感），支持 Drive、Warmth 和 Tone 控制。
/// </summary>
public class TapeSaturationProcessor : IAudioEffect
{
    public int Priority => 40;
    public bool Enabled { get; set; }

    private float _driveDb = 6f;       // 0 ~ +24 dB
    private float _warmth = 0.5f;      // 0 ~ 1 (Wet/Dry 混合)
    private int _tone = 0;             // -100 ~ +100 (负=warm, 正=bright)

    // 内部
    private float _driveLinear = 2f;
    private float _sampleRate = 44100f;

    // Tone 滤波器状态 (一阶倾斜)
    private float _toneFilterL, _toneFilterR;
    private float _toneCoeff;

    public float DriveDb
    {
        get => _driveDb;
        set
        {
            _driveDb = Math.Clamp(value, 0f, 24f);
            _driveLinear = MathF.Pow(10f, _driveDb / 20f);
        }
    }

    public float Warmth
    {
        get => _warmth;
        set => _warmth = Math.Clamp(value, 0f, 1f);
    }

    public int Tone
    {
        get => _tone;
        set
        {
            _tone = Math.Clamp(value, -100, 100);
            RecomputeToneCoeff();
        }
    }

    public TapeSaturationProcessor()
    {
        _driveLinear = MathF.Pow(10f, _driveDb / 20f);
        RecomputeToneCoeff();
    }

    public void SetSampleRate(float sampleRate)
    {
        if (MathF.Abs(sampleRate - _sampleRate) < 0.1f) return;
        _sampleRate = sampleRate;
        RecomputeToneCoeff();
    }

    public void Reset()
    {
        _toneFilterL = 0f;
        _toneFilterR = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        float drive = _driveLinear;
        float wet = _warmth;
        float dry = 1f - wet;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;

            // L 通道
            float dryL = samples[idx];
            float satL = MathF.Tanh(dryL * drive);
            // Tone 滤波器
            _toneFilterL = _toneCoeff * _toneFilterL + (1f - _toneCoeff) * satL;
            float toneOutL = satL + (satL - _toneFilterL) * (_tone / 100f);
            samples[idx] = dryL * dry + toneOutL * wet;

            // R 通道
            float dryR = samples[idx + 1];
            float satR = MathF.Tanh(dryR * drive);
            _toneFilterR = _toneCoeff * _toneFilterR + (1f - _toneCoeff) * satR;
            float toneOutR = satR + (satR - _toneFilterR) * (_tone / 100f);
            samples[idx + 1] = dryR * dry + toneOutR * wet;
        }
    }

    private void RecomputeToneCoeff()
    {
        // Tone 滤波器的截止频率约 800Hz
        float freq = 800f;
        float rc = 1f / (2f * MathF.PI * freq);
        float dt = 1f / _sampleRate;
        _toneCoeff = rc / (rc + dt);
    }
}
