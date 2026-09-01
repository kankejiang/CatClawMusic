namespace CatClawMusic.Maui.Services.Frosted;

/// <summary>
/// 流光喷发背景的共享算法内核：把 Halcyon（Apple Music 风格）OS3 fragment shader 的数学
/// 用纯 C# 原样移植，一套代码两端（Android/Windows）共用，改颜色/运动/颗粒一次两端生效。
///
/// 渲染管线（移植自 Halcyon OS3BgFrag）：
/// 1) 像素坐标 → 归一化 uv（flipY），经 bound 裁剪到下方"流光区"，区外用底色；
/// 2) 对全局 uv 叠加 Perlin 值噪声做扰动，得 noiseValue；
/// 3) 4 个光点随时间做 sin/cos 圆周漂移，按距离 smoothstep 径向混合出基础色/alpha；
/// 4) 用噪声把 RGB 转 HSV 做"反色降饱和"，再叠加 lightOffset 提亮；
/// 5) 叠加极低幅度梯度噪声颗粒（胶片质感）。
/// 低分辨率缓冲逐帧渲染由两端各自驱动，本类不依赖任何平台 API。
/// </summary>
public static class FrostedFlowProcessor
{
    // 静态 uniform（即 Halcyon BgEffectPainter 里写死的那批默认值）
    public const float TranslateY = 0f;
    public const float AlphaMulti = 1f;
    public const float NoiseScale = 1.5f;
    public const float PointRadiusMulti = 1f;
    public const float LightOffsetDefault = 0f;      // 提亮实际按预设覆盖
    public const float SaturateOffsetDefault = 0f;   // 降饱和实际按预设覆盖
    public const float GrainAmplitude = 1f;          // 1/255 量级

    /// <summary>根据颜色阶段数（可带小数）在预设的 4 个颜色阶段间往返插值，得 16 float RGBA（4 光点）。</summary>
    public static float[] InterpolateColors(FrostedFlowPreset preset, float stage, float[] output)
    {
        int s = ((int)stage % 4 + 4) % 4;
        float frac = stage - (int)Math.Floor(stage);
        int startIdx = ColorStageIndex(s);
        int endIdx = ColorStageIndex(s + 1);
        var start = preset.ColorStages[startIdx];
        var end = preset.ColorStages[endIdx];
        for (int i = 0; i < 16; i++)
            output[i] = start[i] + (end[i] - start[i]) * frac;
        return output;
    }

    // 映射：0→colors2(下标1)、1→colors1(下标0)、2→colors2(下标1)、3→colors3(下标2)，其余循环
    private static int ColorStageIndex(int i)
    {
        switch ((i % 4 + 4) % 4)
        {
            case 0: return 1;
            case 1: return 0;
            case 2: return 1;
            default: return 2;
        }
    }

    /// <summary>把一帧流光渲染进 ARGB 像素缓冲。</summary>
    public static void Render(
        int[] argb, int w, int h,
        FrostedFlowPreset preset,
        float[] colors,
        float animTime,
        float boundX, float boundY, float boundW, float boundH,
        int baseArgb)
    {
        float invW = 1f / w, invH = 1f / h;
        float saturateOffset = preset.SaturateOffset;
        float lightOffset = preset.LightOffset;
        float pointOffset = preset.PointOffset;
        var points = preset.Points;

        // 光点先逐点做圆周漂移（一次算好，供逐像素复用）
        var px4 = new float[4]; var py4 = new float[4]; var pr4 = new float[4];
        for (int i = 0; i < 4; i++)
        {
            float ox = points[i * 3];
            float oy = points[i * 3 + 1];
            px4[i] = ox + (float)Math.Sin(animTime + oy) * pointOffset;
            py4[i] = oy + (float)Math.Cos(animTime + ox) * pointOffset;
            pr4[i] = points[i * 3 + 2] * PointRadiusMulti;
        }

        float nShift = -animTime;
        float nScale = NoiseScale;

        for (int y = 0; y < h; y++)
        {
            float vuvY = 1f - y * invH;
            int rowBase = y * w;
            for (int x = 0; x < w; x++)
            {
                float vuvX = x * invW;

                // 流光区外的像素直接用底色，跳过光照计算
                float uvx = (vuvX - boundX) / boundW;
                float uvy = (vuvY - TranslateY - boundY) / boundH;
                if (uvx < 0f || uvx > 1f || uvy < 0f || uvy > 1f)
                {
                    argb[rowBase + x] = baseArgb;
                    continue;
                }

                float noiseValue = Perlin(vuvX * nScale + nShift, vuvY * nScale + nShift);
                float oppositeNoise = SmoothStep(0f, 1f, noiseValue);

                float colR = 0, colG = 0, colB = 0, colA = 0;
                for (int i = 0; i < 4; i++)
                {
                    int ci = i * 4;
                    float pA = colors[ci + 3];
                    float r = colors[ci] * pA;
                    float g = colors[ci + 1] * pA;
                    float b = colors[ci + 2] * pA;
                    float ddx = uvx - px4[i];
                    float ddy = uvy - py4[i];
                    float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                    float pct = SmoothStep(pr4[i], 0f, d);
                    colR += (r - colR) * pct;
                    colG += (g - colG) * pct;
                    colB += (b - colB) * pct;
                    colA += (pA - colA) * pct;
                }

                if (colA > 1e-4f)
                {
                    // 颜色 / alpha（反 premultiply）
                    float invA = 1f / colA;
                    colR *= invA; colG *= invA; colB *= invA;

                    // 反色降饱和：desat = oppositeNoise * saturateOffset，对新饱和度做 mix(s, 0, desat)
                    float lum = colR * 0.299f + colG * 0.587f + colB * 0.114f;
                    float desat = oppositeNoise * saturateOffset;
                    float k = 1f - desat;
                    colR = lum + (colR - lum) * k;
                    colG = lum + (colG - lum) * k;
                    colB = lum + (colB - lum) * k;

                    // 噪声提亮
                    float light = oppositeNoise * lightOffset;
                    colR += light; colG += light; colB += light;

                    colA = colA < 0f ? 0f : colA > 1f ? 1f : colA;
                    colA *= AlphaMulti;

                    // 胶片颗粒
                    float grain = Hash(vuvX * 1e5f, vuvY * 1e5f) * GrainAmplitude / 255f - (0.5f / 255f);
                    colR += grain; colG += grain; colB += grain;

                    argb[rowBase + x] = Pack(
                        ClampByte(colR * colA), ClampByte(colG * colA), ClampByte(colB * colA), ClampByte(colA));
                }
                else
                {
                    argb[rowBase + x] = baseArgb;
                }
            }
        }
    }

    public static int Pack(int r, int g, int b, int a) => (a << 24) | (r << 16) | (g << 8) | b;

    private static int ClampByte(float v) => v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f);

    private static float SmoothStep(float e0, float e1, float x)
    {
        float t = (x - e0) / (e1 - e0);
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return t * t * (3f - 2f * t);
    }

    /// <summary>hash(vec2)：位置抖动出的稳定 [0,1) 伪随机数，作为值噪声采样基底。</summary>
    private static float Hash(float x, float y)
    {
        float hx = Frac(x * 0.1031f);
        float hy = Frac(y * 0.1031f);
        float p = Frac(hx * hy + 0.1031f);
        float a = hx + hy + p;
        float c = (hx * a + hy * a) * 7.17f;
        return Frac(c + a);
    }

    /// <summary>Perlin 值噪声：四角 hash 双线性插值 + smoothstep 平滑。</summary>
    private static float Perlin(float x, float y)
    {
        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float a = Hash(ix, iy);
        float b = Hash(ix + 1, iy);
        float c = Hash(ix, iy + 1);
        float d = Hash(ix + 1, iy + 1);
        float u = fx * fx * (3f - 2f * fx);
        float v = fy * fy * (3f - 2f * fy);
        float bottom = a + (b - a) * u;
        float top = c + (d - c) * u;
        return bottom + (top - bottom) * v;
    }

    private static float Frac(float v) => v - (float)Math.Floor(v);
}