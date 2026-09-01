namespace CatClawMusic.Maui.Services.Frosted;

/// <summary>
/// Halcyon 流光喷发背景预设（移植自 Halcyon 的 BgEffectConfig）。
/// 浅色/深色两套，决定光点位置、光源半径与 4 个颜色阶段的 RGBA（每阶段 4 光点 × RGBA = 16 float），
/// 以及动画/调色参数。两端（Android/Windows）共用同一份配置，一次修改两端生效。
/// </summary>
public sealed class FrostedFlowPreset
{
    /// <summary>4 个光点：[x, y, 半径] * 4（归一化坐标）</summary>
    public float[] Points = new float[12];

    /// <summary>4 个颜色阶段（每阶段 16 float = 4 光点 RGBA），颜色随时间在这之间往返插值</summary>
    public float[][] ColorStages = new float[4][];

    /// <summary>颜色阶段停留/过渡的半周期（秒）</summary>
    public float ColorInterpPeriod = 5f;

    public float LightOffset;
    public float SaturateOffset;
    public float PointOffset;
    public float ShadowColorMulti = 0.3f;
    public float ShadowColorOffset = 0.3f;
    public float ShadowNoiseScale = 5f;

    public static readonly FrostedFlowPreset Light = new()
    {
        Points = new float[]
        {
            0.8f, 0.2f, 1.0f,
            0.8f, 0.9f, 1.0f,
            0.2f, 0.9f, 1.0f,
            0.2f, 0.2f, 1.0f,
        },
        ColorStages = new float[][]
        {
            new float[] { 1.0f, 0.9f, 0.94f, 1.0f, 1.0f, 0.84f, 0.89f, 1.0f, 0.97f, 0.73f, 0.82f, 1.0f, 0.64f, 0.65f, 0.98f, 1.0f },
            new float[] { 0.58f, 0.74f, 1.0f, 1.0f, 1.0f, 0.9f, 0.93f, 1.0f, 0.74f, 0.76f, 1.0f, 1.0f, 0.97f, 0.77f, 0.84f, 1.0f },
            new float[] { 0.58f, 0.74f, 1.0f, 1.0f, 1.0f, 0.9f, 0.93f, 1.0f, 0.74f, 0.76f, 1.0f, 1.0f, 0.97f, 0.77f, 0.84f, 1.0f },
            new float[] { 0.98f, 0.86f, 0.9f, 1.0f, 0.6f, 0.73f, 0.98f, 1.0f, 0.92f, 0.93f, 1.0f, 1.0f, 0.56f, 0.69f, 1.0f, 1.0f },
        },
        ColorInterpPeriod = 5f,
        LightOffset = 0.1f,
        SaturateOffset = 0.2f,
        PointOffset = 0.2f,
    };

    public static readonly FrostedFlowPreset Dark = new()
    {
        Points = new float[]
        {
            0.8f, 0.2f, 1.0f,
            0.8f, 0.9f, 1.0f,
            0.2f, 0.9f, 1.0f,
            0.2f, 0.2f, 1.0f,
        },
        ColorStages = new float[][]
        {
            new float[] { 0.2f, 0.06f, 0.88f, 0.4f, 0.3f, 0.14f, 0.55f, 0.5f, 0.0f, 0.64f, 0.96f, 0.5f, 0.11f, 0.16f, 0.83f, 0.4f },
            new float[] { 0.07f, 0.15f, 0.79f, 0.5f, 0.62f, 0.21f, 0.67f, 0.5f, 0.06f, 0.25f, 0.84f, 0.5f, 0.0f, 0.2f, 0.78f, 0.5f },
            new float[] { 0.07f, 0.15f, 0.79f, 0.5f, 0.62f, 0.21f, 0.67f, 0.5f, 0.06f, 0.25f, 0.84f, 0.5f, 0.0f, 0.2f, 0.78f, 0.5f },
            new float[] { 0.58f, 0.3f, 0.74f, 0.4f, 0.27f, 0.18f, 0.6f, 0.5f, 0.66f, 0.26f, 0.62f, 0.5f, 0.12f, 0.16f, 0.7f, 0.6f },
        },
        ColorInterpPeriod = 8f,
        LightOffset = 0.0f,
        SaturateOffset = 0.17f,
        PointOffset = 0.4f,
    };

    public static FrostedFlowPreset Choose(bool isDark) => isDark ? Dark : Light;
}