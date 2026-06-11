using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 紧凑圆形取色盘：小圆饼选色相/饱和度 + 亮度/透明度滑块
/// </summary>
public class ColorPickerView : LinearLayout
{
    public event Action<Color>? ColorChanged;

    private float _hue = 0f;
    private float _saturation = 1f;
    private float _brightness = 1f;
    private int _alpha = 255;

    private ColorWheelView? _wheel;
    private SeekBar? _brightnessBar;
    private SeekBar? _alphaBar;
    private View? _previewBox;
    private TextView? _hexLabel;
    private TextView? _brightnessValue;
    private TextView? _alphaValue;

    public ColorPickerView(Context context) : base(context)
    {
        Init(context);
    }

    public ColorPickerView(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }

    public void SetColor(Color c)
    {
        _alpha = c.A;
        var hsv = new float[3];
        Android.Graphics.Color.ColorToHSV(new Android.Graphics.Color(c.ToArgb()), hsv);
        _hue = hsv[0];
        _saturation = hsv[1];
        _brightness = hsv[2];
        SyncUI();
        NotifyChanged();
    }

    public Color GetColor()
    {
        var hsv = new[] { _hue, _saturation, _brightness };
        return new Color(Android.Graphics.Color.HSVToColor(_alpha, hsv));
    }

    private void Init(Context ctx)
    {
        Orientation = Orientation.Horizontal;
        SetGravity(GravityFlags.CenterVertical);
        var dp = (int)ctx.Resources!.DisplayMetrics!.Density;

        // 左侧：圆形色轮
        _wheel = new ColorWheelView(this);
        var wheelSize = dp * 100;
        AddView(_wheel, new LayoutParams(wheelSize, wheelSize));

        // 右侧：滑块 + 预览
        var rightCol = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        rightCol.SetPadding(dp * 10, 0, 0, 0);

        // 亮度
        var brightRow = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        brightRow.SetGravity(GravityFlags.CenterVertical);
        var bLabel = new TextView(ctx) { Text = "亮度" };
        bLabel.SetTextColor(Color.ParseColor("#99FFFFFF"));
        bLabel.SetTextSize(ComplexUnitType.Sp, 10f);
        bLabel.SetWidth(dp * 28);
        brightRow.AddView(bLabel);

        _brightnessBar = new SeekBar(ctx) { Max = 100, Progress = 100 };
        _brightnessBar.LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        brightRow.AddView(_brightnessBar);

        _brightnessValue = new TextView(ctx) { Text = "100%" };
        _brightnessValue.SetTextColor(Color.ParseColor("#CCFFFFFF"));
        _brightnessValue.SetTextSize(ComplexUnitType.Sp, 10f);
        _brightnessValue.SetWidth(dp * 30);
        _brightnessValue.Gravity = GravityFlags.End;
        brightRow.AddView(_brightnessValue);
        rightCol.AddView(brightRow);

        // 透明度
        var alphaRow = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        alphaRow.SetGravity(GravityFlags.CenterVertical);
        var aLabel = new TextView(ctx) { Text = "透明" };
        aLabel.SetTextColor(Color.ParseColor("#99FFFFFF"));
        aLabel.SetTextSize(ComplexUnitType.Sp, 10f);
        aLabel.SetWidth(dp * 28);
        alphaRow.AddView(aLabel);

        _alphaBar = new SeekBar(ctx) { Max = 255, Progress = 255 };
        _alphaBar.LayoutParameters = new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        alphaRow.AddView(_alphaBar);

        _alphaValue = new TextView(ctx) { Text = "100%" };
        _alphaValue.SetTextColor(Color.ParseColor("#CCFFFFFF"));
        _alphaValue.SetTextSize(ComplexUnitType.Sp, 10f);
        _alphaValue.SetWidth(dp * 30);
        _alphaValue.Gravity = GravityFlags.End;
        alphaRow.AddView(_alphaValue);
        rightCol.AddView(alphaRow);

        // 预览
        var previewRow = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        previewRow.SetGravity(GravityFlags.CenterVertical);
        previewRow.SetPadding(0, dp * 4, 0, 0);
        _previewBox = new View(ctx);
        var pvLp = new LayoutParams(dp * 28, dp * 28);
        pvLp.RightMargin = dp * 6;
        _previewBox.LayoutParameters = pvLp;
        previewRow.AddView(_previewBox);

        _hexLabel = new TextView(ctx);
        _hexLabel.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        _hexLabel.SetTextSize(ComplexUnitType.Sp, 11f);
        _hexLabel.Gravity = GravityFlags.CenterVertical;
        previewRow.AddView(_hexLabel, new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent));
        rightCol.AddView(previewRow);

        AddView(rightCol, new LayoutParams(0, LayoutParams.WrapContent) { Weight = 1 });

        // 事件
        _brightnessBar.ProgressChanged += (s, e) =>
        {
            _brightness = e.Progress / 100f;
            if (_brightnessValue != null) _brightnessValue.Text = $"{e.Progress}%";
            _wheel?.RefreshBrightness();
            NotifyChanged();
        };
        _alphaBar.ProgressChanged += (s, e) =>
        {
            _alpha = e.Progress;
            if (_alphaValue != null) _alphaValue.Text = $"{(int)(e.Progress / 255f * 100)}%";
            NotifyChanged();
        };

        NotifyChanged();
    }

    private void SyncUI()
    {
        if (_brightnessBar != null) _brightnessBar.Progress = (int)(_brightness * 100);
        if (_alphaBar != null) _alphaBar.Progress = _alpha;
        if (_brightnessValue != null) _brightnessValue.Text = $"{(int)(_brightness * 100)}%";
        if (_alphaValue != null) _alphaValue.Text = $"{(int)(_alpha / 255f * 100)}%";
        _wheel?.Invalidate();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var c = GetColor();
        if (_previewBox != null)
        {
            var gd = new GradientDrawable();
            gd.SetShape(ShapeType.Rectangle);
            gd.SetCornerRadius(6f * Resources!.DisplayMetrics!.Density);
            gd.SetColor(c);
            gd.SetStroke((int)(1f * Resources.DisplayMetrics.Density), Color.ParseColor("#33FFFFFF"));
            _previewBox.Background = gd;
        }
        if (_hexLabel != null)
            _hexLabel.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void NotifyChanged()
    {
        UpdatePreview();
        ColorChanged?.Invoke(GetColor());
    }

    // ========== 圆形色轮 ==========
    private class ColorWheelView : View
    {
        private readonly ColorPickerView _picker;
        private readonly Paint _wheelPaint = new() { AntiAlias = true };
        private readonly Paint _cursorPaint = new() { AntiAlias = true };
        private readonly Paint _cursorBorder = new() { AntiAlias = true };
        private Bitmap? _wheelBitmap;
        private int _bmpSize;

        public ColorWheelView(ColorPickerView picker) : base(picker.Context)
        {
            _picker = picker;
            _cursorBorder.SetStyle(Paint.Style.Stroke);
            _cursorBorder.StrokeWidth = 2.5f;
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            RebuildBitmap();
        }

        private void RebuildBitmap()
        {
            var size = Math.Min(Width, Height);
            if (size <= 0 || size == _bmpSize) return;
            _bmpSize = size;
            _wheelBitmap?.Recycle();
            _wheelBitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!);

            var cx = size / 2f;
            var cy = size / 2f;
            var radius = size / 2f - 2f;
            var pixels = new int[size * size];
            var brightness = _picker._brightness;

            for (int y = 0; y < size; y++)
            {
                float dy = y - cy;
                int rowOff = y * size;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > radius)
                    {
                        pixels[rowOff + x] = 0; // transparent
                        continue;
                    }
                    float angle = (MathF.Atan2(dy, dx) * 180f / MathF.PI + 360f) % 360f;
                    float sat = dist / radius;
                    var hsv = new[] { angle, sat, brightness };
                    pixels[rowOff + x] = Android.Graphics.Color.HSVToColor(255, hsv);
                }
            }
            _wheelBitmap.SetPixels(pixels, 0, size, 0, 0, size, size);
        }

        /// <summary>亮度变化时刷新色轮（复用已有位图）</summary>
        public void RefreshBrightness()
        {
            if (_wheelBitmap == null || _bmpSize <= 0) return;
            var size = _bmpSize;
            var cx = size / 2f;
            var cy = size / 2f;
            var radius = size / 2f - 2f;
            var pixels = new int[size * size];
            var brightness = _picker._brightness;

            for (int y = 0; y < size; y++)
            {
                float dy = y - cy;
                int rowOff = y * size;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > radius)
                    {
                        pixels[rowOff + x] = 0;
                        continue;
                    }
                    float angle = (MathF.Atan2(dy, dx) * 180f / MathF.PI + 360f) % 360f;
                    float sat = dist / radius;
                    pixels[rowOff + x] = Android.Graphics.Color.HSVToColor(255, new[] { angle, sat, brightness });
                }
            }
            _wheelBitmap.SetPixels(pixels, 0, size, 0, 0, size, size);
            Invalidate();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (_wheelBitmap == null) RebuildBitmap();
            if (_wheelBitmap != null)
                canvas.DrawBitmap(_wheelBitmap, 0, 0, _wheelPaint);

            // 光标
            var size = Math.Min(Width, Height);
            if (size <= 0) return;
            var cx = size / 2f;
            var cy = size / 2f;
            var radius = size / 2f - 2f;
            var angle = _picker._hue * MathF.PI / 180f;
            var dist = _picker._saturation * radius;
            var px = cx + dist * MathF.Cos(angle);
            var py = cy + dist * MathF.Sin(angle);

            _cursorBorder.Color = Color.White;
            canvas.DrawCircle(px, py, 8f, _cursorBorder);
            var hsv = new[] { _picker._hue, _picker._saturation, _picker._brightness };
            _cursorPaint.Color = new Color(Android.Graphics.Color.HSVToColor(_picker._alpha, hsv));
            canvas.DrawCircle(px, py, 6f, _cursorPaint);
        }

        public override bool OnTouchEvent(MotionEvent e)
        {
            var action = e.ActionMasked;
            if (action == MotionEventActions.Down || action == MotionEventActions.Move)
            {
                var size = Math.Min(Width, Height);
                if (size <= 0) return true;
                var cx = size / 2f;
                var cy = size / 2f;
                var radius = size / 2f - 2f;
                float dx = e.GetX() - cx;
                float dy = e.GetY() - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                _picker._hue = ((MathF.Atan2(dy, dx) * 180f / MathF.PI) + 360f) % 360f;
                _picker._saturation = Math.Clamp(dist / radius, 0f, 1f);
                _picker.NotifyChanged();
                Invalidate();
                return true;
            }
            return base.OnTouchEvent(e);
        }
    }
}
