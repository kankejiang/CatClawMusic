using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 立体声扩展处理器 — Mid/Side 处理。
/// 正 Width 扩展立体声，负 Width 收窄。
/// </summary>
public class StereoWidenerProcessor : IAudioEffect
{
    public int Priority => 30;
    public bool Enabled { get; set; }

    private float _widthPercent;  // -100 ~ +100

    /// <summary>宽度: -100% (单声道) ~ 0% (无变化) ~ +100% (最大扩展)</summary>
    public float Width
    {
        get => _widthPercent;
        set => _widthPercent = Math.Clamp(value, -100f, 100f);
    }

    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        // width_factor: -100% → 0.0, 0% → 1.0, +100% → 2.0
        float factor = 1f + _widthPercent / 100f;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float L = samples[idx];
            float R = samples[idx + 1];

            float mid = (L + R) * 0.5f;
            float side = (L - R) * 0.5f;

            side *= factor;

            samples[idx] = mid + side;
            samples[idx + 1] = mid - side;
        }
    }

    public void SetSampleRate(float sampleRate) { }
    public void Reset() { }
}
