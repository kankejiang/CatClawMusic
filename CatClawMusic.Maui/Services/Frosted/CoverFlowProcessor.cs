using System;

namespace CatClawMusic.Maui.Services.Frosted;

/// <summary>
/// 复刻 Halcyon宝AppleCoverFlowBackground 的低分辨率"封面流"帧渲染，纯 C#，Android 与 Windows 共用。
/// 流程（与 Halcyon createAppleFlowFrameBitmap 一致）：
///   源封面 → 过饱和 2.5x → 三份反方向慢速旋转的位置错开平铺 → 叠加 wash(黑)`/白洗色) → 两趟盒式模糊 → 中心裁剪。
/// 所有像素运算都在超低分辨率画布上做（~视口/16~24），两端各自把结果放大铺满屏幕。
/// 封面本身才是背景，因此观感鲜(ind acc 而不再灰暗。
/// </summary>
public static class CoverFlowProcessor
{
    /// <summary>流描绘制源封面的长边上限（Halcyon scaledForFlowSource 256）</summary>
    public const int SourceMaxDim = 256;
    /// <summary>封面过饱和度系数（HSV 增强，保色相/保明度）。
    /// ⚠ 不能用 Halcyon 的 ColorMatrix.setSaturation(2.5)：亮度保持矩阵在 2.5x 时，
    /// 权重最高的绿通道会先被压到 0、红推满、蓝残留——暖金(230,180,60) 直接变品红(255,0,99)。
    /// Halcyon 靠视口 1/24 的极致模糊 + 0.34/0.18 深色洗色掩盖了这种偏色，我们的帧更清晰盖不住。
    /// HSV 方案色相零漂移：暖金永远保持金橙调，只是纯度更高，配合洗色/底色统一氛围。</summary>
    public const float Saturation = 1.6f;

    private const float Overscan = 1.3f;   // 旋转时不露边的外扩系数
    private const float Pi = 3.14159265f;

    /// <summary>Halcyon palette.middle：封面流帧的不透明底色。
    /// 深色 = accent 压暗 66%（×0.34，深玫瑰/深青等浓郁暗调）；
    /// 亮色 = accent 向白提亮 72%（pastel）。封面帧本身透明地盖在它之上，
    /// 模糊混合后整体向这个统一色调收拢——这是 Apple Music 沉浸感的关键。</summary>
    public static (int R, int G, int B) MiddleColor(int accentArgb, bool isDark)
    {
        int ar = (accentArgb >> 16) & 0xFF, ag = (accentArgb >> 8) & 0xFF, ab = accentArgb & 0xFF;
        if ((accentArgb & 0xFFFFFF) == 0) { ar = 11; ag = 13; ab = 32; }
        if (isDark)
            return ((int)(ar * 0.34f), (int)(ag * 0.34f), (int)(ab * 0.34f));
        return ((int)(ar + (255 - ar) * 0.72f),
                (int)(ag + (255 - ag) * 0.72f),
                (int)(ab + (255 - ab) * 0.72f));
    }

    /// <summary>Halcyon 帧下采样系数：高 dpi(≥420) 视口 1/24、否则 1/16；Windows(dpi≤0) 用 1/18。
    /// 封面流本质是"放大铺满的重度模糊图"，超低分辨率即可（像素量 ≈ 1/576），模糊块反而更接近原版。</summary>
    public static (int W, int H) SuggestBufferSize(int viewW, int viewH, int densityDpi)
    {
        float factor = densityDpi <= 0 ? 18f : (densityDpi >= 420 ? 24f : 16f);
        int w = Math.Max(1, (int)Math.Round(viewW / factor));
        int h = Math.Max(1, (int)Math.Round(viewH / factor));
        return (w, h);
    }

    /// <summary>ARGB 全不透明掩码（常量级，避免 0xFF000000 被当 uint 触发 int→long 提升）。</summary>
    private const int Opaque = unchecked((int)0xFF000000);

    /// <summary>过饱和且规整后的封面源（≤256 长边，ARGB）。</summary>
    public struct CoverSource
    {
        public int[] Argb;
        public int Width;
        public int Height;
        public bool IsEmpty => Argb == null || Argb.Length == 0;
    }

    /// <summary>渲染出的一帧封面流（ARGB 不透明，尺寸=中心裁剪后的画布）。</summary>
    public struct CoverFrame
    {
        public int[] Pixels;
        public int Width;
        public int Height;
        public bool IsEmpty => Pixels == null || Pixels.Length == 0;
    }

    /// <summary>把任意分辨率 ARGB 封面双线性下采样到 ≤maxDim 长边，作为流描绘制的源。</summary>
    public static CoverSource ScaleSource(int[] srcArgb, int srcW, int srcH, int maxDim = SourceMaxDim)
    {
        if (srcArgb == null || srcW <= 0 || srcH <= 0) return default;
        int longest = Math.Max(srcW, srcH);
        if (longest <= maxDim)
            return new CoverSource { Argb = srcArgb, Width = srcW, Height = srcH };

        float scale = (float)maxDim / longest;
        int tw = Math.Max(1, (int)Math.Round(srcW * scale));
        int th = Math.Max(1, (int)Math.Round(srcH * scale));
        var outPx = new int[tw * th];
        for (int y = 0; y < th; y++)
        {
            float sy = (y + 0.5f) * srcH / th - 0.5f;
            int sy0 = Math.Clamp((int)Math.Floor(sy), 0, srcH - 1);
            int sy1 = Math.Min(sy0 + 1, srcH - 1);
            float fy = Clamp01(sy - sy0);
            for (int x = 0; x < tw; x++)
            {
                float sx = (x + 0.5f) * srcW / tw - 0.5f;
                int sx0 = Math.Clamp((int)Math.Floor(sx), 0, srcW - 1);
                int sx1 = Math.Min(sx0 + 1, srcW - 1);
                float fx = Clamp01(sx - sx0);
                int c00 = srcArgb[sy0 * srcW + sx0];
                int c01 = srcArgb[sy0 * srcW + sx1];
                int c10 = srcArgb[sy1 * srcW + sx0];
                int c11 = srcArgb[sy1 * srcW + sx1];
                outPx[y * tw + x] = Blend(Blerp(c00, c01, fx), Blerp(c10, c11, fx), fy);
            }
        }
        return new CoverSource { Argb = outPx, Width = tw, Height = th };
    }

    /// <summary>渲染一帧封面流到目标低分辨率画布(outW×outH)。accentArgb=封面强调色(0xRRGGBB，缺省 0)；isDark=深色预设。</summary>
    public static CoverFrame Render(CoverSource src, int outW, int outH, long timeMs,
        int densityDpi, float blur, int accentArgb, bool isDark)
    {
        if (src.IsEmpty || outW <= 0 || outH <= 0) return default;

        // 1) 过饱和源（只算一次每曲）
        int n = src.Width * src.Height;
        var sat = new int[n];
        for (int i = 0; i < n; i++) sat[i] = ApplySaturation(src.Argb[i], Saturation);

        // 2) 工作画布 = 目标画布 * 外扩（旋转不露边）
        int workW = Math.Max(1, (int)Math.Round(outW * Overscan));
        int workH = Math.Max(1, (int)Math.Round(outH * Overscan));
        var frame = new int[workW * workH];
        // 不透明 palette.middle 底色（Halcyon：Box.background(palette.middle)，帧位图透明地盖在其上）。
        // 封面没铺到的角落、模糊的透明混合都向这个统一的深色/ pastel 色调收拢，而不是塌成黑色。
        var (mr, mg, mb) = MiddleColor(accentArgb, isDark);
        int middleArgb = Opaque | (mr << 16) | (mg << 8) | mb;
        Array.Fill(frame, middleArgb);

        float diagonal = Math.Max(workW, workH) * Overscan;
        float coverScale = diagonal / Math.Max(src.Height, 1);
        float translateX = -(diagonal - workW) / 2f;
        float translateY = -(diagonal - workH) / 2f;
        float rotatePivot = diagonal / 2f;
        float cx = workW / 2f;
        float cy = workH / 2f;

        float rot = (timeMs % 70_000L) / 70_000f * 360f;

        // 三层：慢速反向旋转 + 错位平铺，越后越贴近"中央主画面"
        DrawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation: (timeMs % 120_000L) / 120_000f * -360f, offsetX: 0f, offsetY: 0f, extraRotation: null);
        DrawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation: (timeMs % 90_000L) / 90_000f * 360f, offsetX: -0.95f, offsetY: -0.7f, extraRotation: null);
        DrawLayer(frame, workW, workH, src, sat, coverScale, rotatePivot, translateX, translateY, cx, cy,
            rotation: rot, offsetX: -0.5f, offsetY: 0.7f, extraRotation: rot);

        // 3) 洗色 wash（Halcyon 原版 alpha：深色 0.34/0.18、亮色 0.26/0.14）。
        //    主洗色 = palette.middle 向黑/白稍收拢后以中高 alpha 整体覆盖，
        //    把过饱和封面统一压进同一氛围调性——这层"面纱"是深色沉浸感的主要来源。
        (int wpR, int wpG, int wpB, float wpAlpha) = Wash(isDark, true, mr, mg, mb);
        (int wsR, int wsG, int wsB, float wsAlpha) = Wash(isDark, false, mr, mg, mb);
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = BlendOver(frame[i], wpR, wpG, wpB, wpAlpha);
            frame[i] = BlendOver(frame[i], wsR, wsG, wsB, wsAlpha);
        }

        // 4) 两趟盒式模糊
        int radius = (int)Math.Round(((BlurClamp(blur) - 30f) / 70f) * 17f + 8f);
        radius = Math.Clamp(radius, 8, 25);
        BoxBlur(frame, workW, workH, radius);

        // 5) 中心裁剪回目标尺寸
        int cropW = (int)Math.Round(workW / Overscan);
        int cropH = (int)Math.Round(workH / Overscan);
        cropW = Math.Clamp(cropW, 1, workW);
        cropH = Math.Clamp(cropH, 1, workH);
        int ox = Math.Max(0, (workW - cropW) / 2);
        int oy = Math.Max(0, (workH - cropH) / 2);
        var result = new int[cropW * cropH];
        for (int y = 0; y < cropH; y++)
            Array.Copy(frame, (oy + y) * workW + ox, result, y * cropW, cropW);

        return new CoverFrame { Pixels = result, Width = cropW, Height = cropH };
    }

    private static void DrawLayer(int[] frame, int workW, int workH, CoverSource src, int[] sat,
        float coverScale, float pivot, float tx, float ty, float cx, float cy,
        float rotation, float offsetX, float offsetY, float? extraRotation)
    {
        // 与 Halcyon 相同的正向变换：Scale → Rot(pivot) → Translate → Translate(offset) → Rot(center)
        var F = new M2D(coverScale, 0, 0, 0, coverScale, 0);            // 只有其他 z 分量；起 S
        F = Mul(Rot(rotation, pivot, pivot), F);                          // Rot * S
        F = Mul(new M2D(1, 0, tx, 0, 1, ty), F);                          // T * (.) 
        if (offsetX != 0f || offsetY != 0f)
            F = Mul(new M2D(1, 0, workW * offsetX, 0, 1, workH * offsetY), F);
        if (extraRotation != null)
            F = Mul(Rot(extraRotation.Value, cx, cy), F);

        // 求逆，供"画布像素→封面像素"采样
        double det = F.a * F.e - F.b * F.d;
        if (Math.Abs(det) < 1e-9) return;

        var satArgb = (int[])sat.Clone(); // 局部引用避免改到共享
        var sh = (double)src.Height;
        var sw = (double)src.Width;
        var stride = src.Width;
        // 预取源像素指针直接索引饱和数组
        var sArr = satArgb;

        for (int fy = 0; fy < workH; fy++)
        {
            int row = fy * workW;
            double fyD = fy;
            double ny = (F.d * (fyD) - F.e * 0 - (F.d * F.c - F.e * F.a) * 0); // noop
            // 直接解算
            // fx = a*x + b*y + c ; fy = d*x + e*y + f  => 给定 (px,py) 求 x,y
            for (int fx = 0; fx < workW; fx++)
            {
                double dxc = fx - F.c;
                double dyf = fy - F.f;
                double sx = (F.e * dxc - F.b * dyf) / det;
                double sy = (-F.d * dxc + F.a * dyf) / det;
                if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) continue;
                int sx0 = Math.Min((int)sx, stride - 1);
                int sy0 = Math.Min((int)sy, src.Height - 1);
                int sx1 = Math.Min(sx0 + 1, stride - 1);
                int sy1 = Math.Min(sy0 + 1, src.Height - 1);
                float fxw = (float)(sx - sx0);
                float fyw = (float)(sy - sy0);
                int c00 = sArr[sy0 * stride + sx0];
                int c01 = sArr[sy0 * stride + sx1];
                int c10 = sArr[sy1 * stride + sx0];
                int c11 = sArr[sy1 * stride + sx1];
                frame[row + fx] = Blend(Blerp(c00, c01, fxw), Blerp(c10, c11, fxw), fyw);
            }
        }
    }

    // ===== 变换辅助 =====
    private struct M2D
    {
        public double a, b, c, d, e, f; // fx=a*x+b*y+c ; fy=d*x+e*y+f
        public M2D(double a, double b, double c, double d, double e, double f)
        { this.a = a; this.b = b; this.c = c; this.d = d; this.e = e; this.f = f; }
    }

    /// <summary>左乘：返回 A∘B（先 B 后 A）。</summary>
    private static M2D Mul(M2D A, M2D B)
        => new(A.a * B.a + A.b * B.d,
               A.a * B.b + A.b * B.e,
               A.a * B.c + A.b * B.f + A.c,
               A.d * B.a + A.e * B.d,
               A.d * B.b + A.e * B.e,
               A.d * B.c + A.e * B.f + A.f);

    /// <summary>绕 (px,py) 旋转 deg 度。</summary>
    private static M2D Rot(float deg, float px, float py)
    {
        float rad = deg * Pi / 180f;
        float cs = (float)Math.Cos(rad);
        float sn = (float)Math.Sin(rad);
        return new M2D(cs, -sn, px - px * cs + py * sn,
                       sn, cs, py - py * cs - px * sn);
    }

    // ===== 颜色辅助 =====
    private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static int Blerp(int ca, int cb, float t)
    {
        int ar = (ca >> 16) & 0xFF, ag = (ca >> 8) & 0xFF, ab = ca & 0xFF;
        int br = (cb >> 16) & 0xFF, bg = (cb >> 8) & 0xFF, bb = cb & 0xFF;
        return Opaque
            | ((int)(ar + (br - ar) * t) << 16)
            | ((int)(ag + (bg - ag) * t) << 8)
            | (int)(ab + (bb - ab) * t);
    }

    /// <summary>按 t 线性混合两个不透明颜色。</summary>
    private static int Blend(int ca, int cb, float t)
        => Blerp(ca, cb, t);

    /// <summary>把半透明颜色(src)以 SRC_OVER 叠加到 dst 之上（Halcyon drawColor）。src 仅 RGB 有效时需 alpha 单独给。</summary>
    private static int BlendOver(int dst, int sr, int sg, int sb, float a)
    {
        float inv = 1f - a;
        int dr = (dst >> 16) & 0xFF, dg = (dst >> 8) & 0xFF, db = dst & 0xFF;
        int r = (int)(dr * inv + sr * a);
        int g = (int)(dg * inv + sg * a);
        int b = (int)(db * inv + sb * a);
        return Opaque | (r << 16) | (g << 8) | b;
    }

    /// <summary>HSV 饱和度增强（保色相、保明度），对单个 ARGB 像素应用。
    /// 色相零漂移 → 暖金/肤色/橙色系封面不会被过饱和打成品红或灰；饱和 clamp 到 1 自然封顶。
    /// 不做"近中性去色"：JPEG 灰区色度噪声经 1/24 低分辨率 + 重模糊 + 深色洗色后完全不可见，
    /// 而去色 hack 会把封面里大片暖白/肤色（低饱和但有色相）打成死灰，让背景失去发色层次。</summary>
    private static int ApplySaturation(int argb, float s)
    {
        int r = (argb >> 16) & 0xFF;
        int g = (argb >> 8) & 0xFF;
        int b = argb & 0xFF;
        RgbToHsv(r, g, b, out float h, out float sat, out float v);
        sat = Math.Min(sat * s, 1f);
        HsvToRgb(h, sat, v, out int rr, out int gg, out int bb);
        return Opaque | (rr << 16) | (gg << 8) | bb;
    }

    private static void RgbToHsv(int r, int g, int b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float d = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d < 1e-6f) { h = 0; return; }
        if (max == rf) h = 60f * (((gf - bf) / d) + (gf < bf ? 6f : 0f));
        else if (max == gf) h = 60f * (((bf - rf) / d) + 2f);
        else h = 60f * (((rf - gf) / d) + 4f);
    }

    private static void HsvToRgb(float h, float s, float v, out int r, out int g, out int b)
    {
        if (s <= 0f) { r = g = b = (int)Math.Round(v * 255f); return; }
        h = ((h % 360f) + 360f) % 360f;
        float hi = (float)Math.Floor(h / 60f);
        float f = h / 60f - hi;
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));
        float rr, gg, bb;
        switch ((int)hi)
        {
            case 0: rr = v; gg = t; bb = p; break;
            case 1: rr = q; gg = v; bb = p; break;
            case 2: rr = p; gg = v; bb = t; break;
            case 3: rr = p; gg = q; bb = v; break;
            case 4: rr = t; gg = p; bb = v; break;
            default: rr = v; gg = p; bb = q; break;
        }
        r = (int)Math.Round(rr * 255f);
        g = (int)Math.Round(gg * 255f);
        b = (int)Math.Round(bb * 255f);
    }

    /// <summary>wash 洗色（Halcyon 原版）。入参为 palette.middle 底色：
    /// 深色：主洗 = blend(middle, 黑, 0.28)=middle×0.72，α 0.34；次洗 = 黑 α 0.18。
    /// 亮色：主洗 = blend(middle, 白, 0.22)=middle×0.78+白×0.22，α 0.26；次洗 = 白 α 0.14。</summary>
    private static (int R, int G, int B, float Alpha) Wash(bool isDark, bool primary, int mr, int mg, int mb)
    {
        if (isDark)
        {
            if (primary)
                return ((int)(mr * 0.72f), (int)(mg * 0.72f), (int)(mb * 0.72f), 0.34f);
            return (0, 0, 0, 0.18f);
        }
        if (primary)
            return ((int)(mr * 0.78f + 255 * 0.22f), (int)(mg * 0.78f + 255 * 0.22f), (int)(mb * 0.78f + 255 * 0.22f), 0.26f);
        return (255, 255, 255, 0.14f);
    }

    private static float BlurClamp(float blur) => blur < 30 ? 30 : (blur > 100 ? 100 : blur);

    /// <summary>两趟分离盒式模糊（Halcyon blurBitmapFast），就地修改 argb 数组。</summary>
    private static void BoxBlur(int[] argb, int width, int height, int radius)
    {
        if (width <= 1 || height <= 1 || argb.Length < width * height) return;
        var temp = new int[argb.Length];
        int r = Math.Clamp(radius, 1, 25);
        int window = r * 2 + 1;

        // Horizontal pass: argb -> temp
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            long a = 0, red = 0, grn = 0, blu = 0;
            for (int k = -r; k <= r; k++)
            {
                int p = argb[ClampIdx(row + Math.Clamp(k, 0, width - 1), argb.Length)];
                a += (p >> 24) & 0xFF; red += (p >> 16) & 0xFF; grn += (p >> 8) & 0xFF; blu += p & 0xFF;
            }
            for (int x = 0; x < width; x++)
            {
                temp[row + x] = Pack(a / window, red / window, grn / window, blu / window);
                int xo = Math.Clamp(x - r, 0, width - 1);
                int xi = Math.Clamp(x + r + 1, 0, width - 1);
                int pOut = argb[row + xo];
                int pIn = argb[row + xi];
                a += ((pIn >> 24) & 0xFF) - ((pOut >> 24) & 0xFF);
                red += ((pIn >> 16) & 0xFF) - ((pOut >> 16) & 0xFF);
                grn += ((pIn >> 8) & 0xFF) - ((pOut >> 8) & 0xFF);
                blu += (pIn & 0xFF) - (pOut & 0xFF);
            }
        }

        // Vertical pass: temp -> argb
        for (int x = 0; x < width; x++)
        {
            long a = 0, red = 0, grn = 0, blu = 0;
            for (int k = -r; k <= r; k++)
            {
                int p = temp[Math.Clamp(k, 0, height - 1) * width + x];
                a += (p >> 24) & 0xFF; red += (p >> 16) & 0xFF; grn += (p >> 8) & 0xFF; blu += p & 0xFF;
            }
            for (int y = 0; y < height; y++)
            {
                argb[y * width + x] = Pack(a / window, red / window, grn / window, blu / window);
                int yo = Math.Clamp(y - r, 0, height - 1);
                int yi = Math.Clamp(y + r + 1, 0, height - 1);
                int pOut = temp[yo * width + x];
                int pIn = temp[yi * width + x];
                a += ((pIn >> 24) & 0xFF) - ((pOut >> 24) & 0xFF);
                red += ((pIn >> 16) & 0xFF) - ((pOut >> 16) & 0xFF);
                grn += ((pIn >> 8) & 0xFF) - ((pOut >> 8) & 0xFF);
                blu += (pIn & 0xFF) - (pOut & 0xFF);
            }
        }
        temp = null;
    }

    private static long ClampIdx(int i, int len) => i < 0 ? 0 : (i >= len ? len - 1 : i);

    private static int Pack(long a, long r, long g, long b)
        => ((int)(a & 0xFF) << 24) | ((int)(r & 0xFF) << 16) | ((int)(g & 0xFF) << 8) | (int)(b & 0xFF);
}