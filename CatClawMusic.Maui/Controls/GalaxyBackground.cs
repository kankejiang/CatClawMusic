using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 夜空银河动态背景控件。
/// 复刻 docs/background-design-prototype.html 的「夜空银河」分层：
/// 深空底色 → 主题色星云（双团柔光缓慢漂移）→ 斜贯银河光带（主题色发光）→ 角落强调辉光
/// → 密集星点层（~150 颗随机大小与闪烁）→ 偶发流星 → 暗角。
/// 全部由 PrimaryColor / AccentColor / DeepColor 三个主题色驱动，随主题换肤。
/// 当 IsPaused 或 IsInteractionActive 为 true 时，停止时间推进（冻结当前帧，不再重绘），
/// 以在播放页启动雾面动态背景或用户滑动 Tab 时节省 GPU/主线程开销。
/// </summary>
public class GalaxyBackground : GraphicsView
{
    /// <summary>主题主色（对应原型 --a1）</summary>
    public static readonly BindableProperty PrimaryColorProperty =
        BindableProperty.Create(nameof(PrimaryColor), typeof(Color), typeof(GalaxyBackground), Colors.Purple,
            propertyChanged: OnColorChanged);

    /// <summary>主题强调色（对应原型 --a2）</summary>
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(GalaxyBackground), Colors.Cyan,
            propertyChanged: OnColorChanged);

    /// <summary>深空底色（对应原型 --deep）</summary>
    public static readonly BindableProperty DeepColorProperty =
        BindableProperty.Create(nameof(DeepColor), typeof(Color), typeof(GalaxyBackground), Color.FromArgb("#080B1A"),
            propertyChanged: OnColorChanged);

    /// <summary>是否暂停动态效果（冻结当前帧）。播放页启动雾面背景时设为 true。</summary>
    public static readonly BindableProperty IsPausedProperty =
        BindableProperty.Create(nameof(IsPaused), typeof(bool), typeof(GalaxyBackground), false);

    /// <summary>用户是否正在滑动 Tab。滑动期间暂停动画以释放主线程（与 FrostedBackground 行为一致）。</summary>
    public static readonly BindableProperty IsInteractionActiveProperty =
        BindableProperty.Create(nameof(IsInteractionActive), typeof(bool), typeof(GalaxyBackground), false);

    private readonly GalaxyDrawable _drawable;
    private IDispatcherTimer? _timer;
    private double _time;

    public GalaxyBackground()
    {
        _drawable = new GalaxyDrawable(this);
        Drawable = _drawable;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        StartAnimationLoop();
    }

    /// <summary>主题主色</summary>
    public Color PrimaryColor
    {
        get => (Color)GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    /// <summary>主题强调色</summary>
    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    /// <summary>深空底色</summary>
    public Color DeepColor
    {
        get => (Color)GetValue(DeepColorProperty);
        set => SetValue(DeepColorProperty, value);
    }

    /// <summary>是否暂停动态效果</summary>
    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    /// <summary>用户是否正在滑动 Tab</summary>
    public bool IsInteractionActive
    {
        get => (bool)GetValue(IsInteractionActiveProperty);
        set => SetValue(IsInteractionActiveProperty, value);
    }

    /// <summary>当前动画时间（秒），供绘制层读取</summary>
    internal double Time => _time;

    private void StartAnimationLoop()
    {
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33); // ~30fps，足以表现星云漂移/星点闪烁，且开销可控
        _timer.Tick += (_, _) =>
        {
            if (IsPaused || IsInteractionActive)
                return;
            _time += 0.033;
            _drawable.Time = _time;
            Invalidate();
        };
        _timer.Start();
    }

    private static void OnColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // 主题色变化：即便处于暂停状态也刷新一帧，确保换肤立即生效
        if (bindable is GalaxyBackground g)
        {
            g._drawable.Time = g._time;
            g.Invalidate();
        }
    }

    /// <summary>
    /// 夜空绘制层：所有图形由 ICanvas 实时绘制，避免预渲染资源、随主题色即时变化。
    /// </summary>
    private sealed class GalaxyDrawable : IDrawable
    {
        private readonly GalaxyBackground _owner;
        private readonly List<Star> _stars = new();
        private readonly Random _rnd = new(0x9E37);
        private readonly float _shootingStarStartX;
        private readonly float _shootingStarStartY;
        private readonly float _shootingStarDelay;
        private readonly float _shootingStarMoveX;
        private readonly float _shootingStarMoveY;

        public double Time { get; set; }

        public GalaxyDrawable(GalaxyBackground owner)
        {
            _owner = owner;
            // 预生成 ~150 颗星（固定随机种子，保证布局稳定不每帧抖动）
            const int count = 150;
            for (int i = 0; i < count; i++)
            {
                var size = (float)(_rnd.NextDouble() * 1.8 + 0.6);
                _stars.Add(new Star
                {
                    X = (float)_rnd.NextDouble(),
                    Y = (float)_rnd.NextDouble(),
                    Size = size,
                    BaseOpacity = (float)(_rnd.NextDouble() * 0.6 + 0.4),
                    TwinkleSpeed = (float)(_rnd.NextDouble() * 1.2 + 0.6),
                    TwinklePhase = (float)(_rnd.NextDouble() * Math.PI * 2),
                    Glow = size > 1.7f,
                });
            }

            // 每屏流星参数：起点在左上区域，9s 周期，随机延迟
            _shootingStarStartX = (float)(0.00 + _rnd.NextDouble() * 0.18); // 0% ~ 18% 宽
            _shootingStarStartY = (float)(0.04 + _rnd.NextDouble() * 0.28); // 4% ~ 32% 高
            _shootingStarDelay = (float)(1.0 + _rnd.NextDouble() * 7.0);    // 1s ~ 8s 延迟
            // 移动向量与 35° 尾迹一致（230px, 160px 以原型屏为基准）
            _shootingStarMoveX = 230f;
            _shootingStarMoveY = 160f;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var w = dirtyRect.Width;
            var h = dirtyRect.Height;
            if (w <= 0 || h <= 0) return;

            var primary = _owner.PrimaryColor ?? Colors.Purple;
            var accent = _owner.AccentColor ?? Colors.Cyan;
            var deep = _owner.DeepColor ?? Color.FromArgb("#080B1A");
            var scale = w / 320f; // 以原型 260~320px 屏宽为基准的缩放，保证星点/流星大小随屏自适应
            var t = Time;

            // 1) 深空底色
            canvas.FillColor = deep;
            canvas.FillRectangle(0, 0, w, h);

            // 2) 顶部主题色辉光（对应 .bg-base 的 radial-gradient）
            DrawRadialGlow(canvas, w * 0.5f, -h * 0.15f, Math.Max(w, h) * 0.75f, primary, 0.30f);

            // 3) 星云：双团主题色柔光，缓慢漂移（对应 .bg-nebula::before/::after）
            var driftX1 = (float)Math.Sin(t * 0.045) * w * 0.04f;
            var driftY1 = (float)Math.Cos(t * 0.05) * h * 0.03f;
            DrawRadialGlow(canvas, -w * 0.12f + driftX1, -h * 0.12f + driftY1,
                w * 0.7f, primary, 0.5f);

            var driftX2 = (float)Math.Sin(t * 0.04 + 1.5) * w * 0.045f;
            var driftY2 = (float)Math.Cos(t * 0.052 + 1.0) * h * 0.035f;
            DrawRadialGlow(canvas, w * 1.12f + driftX2, h * 1.16f + driftY2,
                w * 0.66f, accent, 0.45f);

            // 4) 银河光带：斜贯屏幕的发光星河（对应 .bg-galaxy，rotate(-20deg)）
            DrawGalaxyBand(canvas, w, h, t, primary, accent, scale);

            // 5) 角落强调辉光（对应 .bg-glow）
            DrawRadialGlow(canvas, -w * 0.1f, -h * 0.12f, w * 0.7f, primary, 0.42f);
            DrawRadialGlow(canvas, w * 1.1f, h * 1.15f, w * 0.8f, accent, 0.4f);

            // 6) 星点层（对应 .starfield）
            foreach (var s in _stars)
            {
                var tw = 0.18f + 0.82f * (0.5f + 0.5f * (float)Math.Sin(t * s.TwinkleSpeed + s.TwinklePhase));
                var alpha = s.BaseOpacity * tw;
                var r = s.Size * scale;
                var cx = s.X * w;
                var cy = s.Y * h;
                if (s.Glow)
                {
                    DrawRadialGlow(canvas, cx, cy, r * 6f, Colors.White, alpha * 0.5f);
                }
                canvas.FillColor = Colors.White.WithAlpha(alpha);
                canvas.FillCircle(cx, cy, r);
            }

            // 7) 偶发流星（对应 .shooting，9s 循环）
            DrawShootingStar(canvas, w, h, t, scale);

            // 8) 暗角（对应 .bg-vignette）
            DrawVignette(canvas, w, h);
        }

        private static void DrawRadialGlow(ICanvas canvas, float cx, float cy, float diameter, Color color, float alpha)
        {
            var r = diameter / 2f;
            var rect = new RectF(cx - r, cy - r, diameter, diameter);
            var stops = new PaintGradientStop[]
            {
                new(0f, color.WithAlpha(alpha)),
                new(1f, color.WithAlpha(0f)),
            };
            var paint = new RadialGradientPaint(stops, new Point(0.5, 0.5), 0.5);
            canvas.SetFillPaint(paint, rect);
            canvas.FillEllipse(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static void DrawGalaxyBand(ICanvas canvas, float w, float h, double t, Color primary, Color accent, float scale)
        {
            var bandH = h * 0.64f;
            var bandY = h * 0.18f;
            var bandW = w * 2.2f;
            var bandX = -w * 0.6f;
            var drift = (float)Math.Sin(t * 0.05) * w * 0.03f; // 极缓慢横向漂移

            var cx = w * 0.5f;
            var cy = bandY + bandH * 0.5f;

            canvas.SaveState();
            canvas.Translate(cx, cy);
            canvas.Rotate(-20f);
            canvas.Translate(-cx, -cy);

            var rect = new RectF(bandX + drift, bandY, bandW, bandH);

            // 主体光带：原型 opacity 0.9 + blur(24px)，用多层堆叠模拟高斯模糊
            for (int i = 0; i < 5; i++)
            {
                var a = (0.30f - i * 0.045f);
                if (a <= 0) break;
                var inset = i * bandH * 0.14f;
                var r = new RectF(rect.X, rect.Y + inset, rect.Width, rect.Height - inset * 2);
                var stops = new PaintGradientStop[]
                {
                    new(0.00f, accent.WithAlpha(0f)),
                    new(0.35f, accent.WithAlpha(a * 0.55f)),
                    new(0.50f, primary.WithAlpha(a)),
                    new(0.65f, accent.WithAlpha(a * 0.55f)),
                    new(1.00f, accent.WithAlpha(0f)),
                };
                var paint = new LinearGradientPaint(stops, new Point(0, 0.5), new Point(1, 0.5));
                canvas.SetFillPaint(paint, r);
                canvas.FillRectangle(r.X, r.Y, r.Width, r.Height);
            }

            // 核心高亮层：更窄、更亮，强调星河
            var coreRect = new RectF(rect.X, rect.Y + bandH * 0.38f, rect.Width, bandH * 0.24f);
            var coreStops = new PaintGradientStop[]
            {
                new(0.00f, primary.WithAlpha(0f)),
                new(0.42f, accent.WithAlpha(0.35f)),
                new(0.50f, Colors.White.WithAlpha(0.55f)),
                new(0.58f, accent.WithAlpha(0.35f)),
                new(1.00f, primary.WithAlpha(0f)),
            };
            var corePaint = new LinearGradientPaint(coreStops, new Point(0, 0.5), new Point(1, 0.5));
            canvas.SetFillPaint(corePaint, coreRect);
            canvas.FillRectangle(coreRect.X, coreRect.Y, coreRect.Width, coreRect.Height);

            canvas.RestoreState();
        }

        private void DrawShootingStar(ICanvas canvas, float w, float h, double t, float scale)
        {
            const double period = 9.0;
            const double visibleDuration = 2.0; // 0 ~ 2s 可见，与原型 32% 对应
            var local = (t + _shootingStarDelay) % period;
            if (local >= visibleDuration) return;

            float p = (float)(local / visibleDuration); // 0..1
            var startX = _shootingStarStartX * w;
            var startY = _shootingStarStartY * h;
            var moveX = _shootingStarMoveX * scale;
            var moveY = _shootingStarMoveY * scale;
            var sx = startX + moveX * (float)p;
            var sy = startY + moveY * (float)p;

            var len = 130f * scale;
            var hh = 2f * scale;

            // 尾迹角度与运动方向一致（35° 对角）
            var angleDeg = (float)(Math.Atan2(moveY, moveX) * 180.0 / Math.PI);

            // 淡入淡出
            float alpha;
            if (p < 0.18f) alpha = p / 0.18f;          // 0 ~ 18% 淡入
            else alpha = 1f - (p - 0.18f) / 0.82f;      // 18% ~ 100% 淡出

            // 拖尾：自头部沿运动反方向延伸，白色渐隐
            canvas.SaveState();
            canvas.Translate(sx, sy);
            canvas.Rotate(angleDeg);
            var trailRect = new RectF(-len, -hh / 2f, len, hh);
            var stops = new PaintGradientStop[]
            {
                new(0.0f, Colors.White.WithAlpha(0f)),   // 尾端
                new(0.7f, Colors.White.WithAlpha((float)(0.7f * alpha))),
                new(1.0f, Colors.White.WithAlpha((float)alpha)), // 头端
            };
            var paint = new LinearGradientPaint(stops, new Point(0, 0.5), new Point(1, 0.5));
            canvas.SetFillPaint(paint, trailRect);
            canvas.FillRectangle(trailRect.X, trailRect.Y, trailRect.Width, trailRect.Height);
            canvas.RestoreState();

            // 头部亮点 + 柔光
            DrawRadialGlow(canvas, sx, sy, 18f * scale, Colors.White, 0.9f * alpha);
            canvas.FillColor = Colors.White.WithAlpha(alpha);
            canvas.FillCircle(sx, sy, 2.8f * scale);
        }

        private static void DrawVignette(ICanvas canvas, float w, float h)
        {
            var rect = new RectF(0, 0, w, h);
            var stops = new PaintGradientStop[]
            {
                new(0f, Colors.Black.WithAlpha(0f)),
                new(0.52f, Colors.Black.WithAlpha(0f)),
                new(1f, Colors.Black.WithAlpha(0.5f)),
            };
            var paint = new RadialGradientPaint(stops, new Point(0.5, 0.38), 0.7);
            canvas.SetFillPaint(paint, rect);
            canvas.FillRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private sealed class Star
        {
            public float X;
            public float Y;
            public float Size;
            public float BaseOpacity;
            public float TwinkleSpeed;
            public float TwinklePhase;
            public bool Glow;
        }
    }
}
