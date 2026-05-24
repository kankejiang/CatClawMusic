using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Graphics.Drawables.Shapes;
using Android.Views;
using Android.Widget;
using System.Linq;
using Android.Util;
using Android.OS;
using CatClawMusic.UI.Services;

namespace CatClawMusic.UI.Helpers;

public class GlassDialog : Android.App.Dialog
{
    private readonly LinearLayout _cardLayout;
    private readonly float _density;
    private readonly int _dp;
    private LinearLayout? _itemsContainer;
    private EditText? _inputField;
    private readonly Color _themeColor;

    public string? InputText => _inputField?.Text?.Trim();

    public GlassDialog(Context ctx) : base(ctx, Android.Resource.Style.ThemeDeviceDefaultLightNoActionBar)
    {
        _density = ctx.Resources!.DisplayMetrics!.Density;
        _dp = (int)_density;

        var tv = new TypedValue();
        _themeColor = ctx.Theme?.ResolveAttribute(global::Android.Resource.Attribute.ColorPrimary, tv, true) == true
            ? new Color(tv.Data)
            : Color.ParseColor("#9B7ED8");

        RequestWindowFeature((int)WindowFeatures.NoTitle);

        _cardLayout = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        _cardLayout.SetPadding(_dp * 6, _dp * 8, _dp * 6, _dp * 8);

        var bg = new GradientDrawable();
        bg.SetShape(ShapeType.Rectangle);
        bg.SetCornerRadius(24 * _density);
        bg.SetColor(Color.ParseColor("#CC000000"));
        bg.SetStroke(_dp, Color.ParseColor("#33FFFFFF"));
        _cardLayout.Background = bg;
    }

    private static Color WithAlpha(Color c, int alpha)
    {
        return new Color((byte)(c.ToArgb() >> 16 & 0xFF),
            (byte)(c.ToArgb() >> 8 & 0xFF),
            (byte)(c.ToArgb() & 0xFF),
            (byte)alpha);
    }

    public GlassDialog SetTitle(string title, string? subtitle = null)
    {
        var titleTv = new TextView(Context!) { Text = title };
        titleTv.SetTextSize(ComplexUnitType.Sp, 14f);
        titleTv.SetTextColor(Color.White);
        titleTv.SetTypeface(null, TypefaceStyle.Bold);
        titleTv.SetPadding(_dp * 14, _dp * 10, _dp * 14, _dp * 2);
        titleTv.SetSingleLine(true);
        titleTv.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        _cardLayout.AddView(titleTv);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var subTv = new TextView(Context!) { Text = subtitle };
            subTv.SetTextSize(ComplexUnitType.Sp, 12f);
            subTv.SetTextColor(Color.ParseColor("#CCFFFFFF"));
            subTv.SetPadding(_dp * 14, 0, _dp * 14, _dp * 8);
            subTv.SetSingleLine(true);
            subTv.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            _cardLayout.AddView(subTv);
        }
        else
        {
            titleTv.SetPadding(_dp * 14, _dp * 12, _dp * 14, _dp * 12);
        }

        AddDivider();
        return this;
    }

    public GlassDialog AddInput(string hint = "")
    {
        var container = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        container.SetPadding(_dp * 14, _dp * 8, _dp * 14, _dp * 8);

        _inputField = new EditText(Context!)
        {
            Hint = hint,
            InputType = Android.Text.InputTypes.TextFlagCapSentences
        };
        _inputField.SetTextSize(ComplexUnitType.Sp, 14f);
        _inputField.SetTextColor(Color.White);
        _inputField.SetHintTextColor(Color.ParseColor("#80FFFFFF"));
        _inputField.SetBackgroundColor(Color.Transparent);
        _inputField.SetPadding(_dp * 4, _dp * 4, _dp * 4, _dp * 4);

        var inputBg = new GradientDrawable();
        inputBg.SetShape(ShapeType.Rectangle);
        inputBg.SetCornerRadius(12 * _density);
        inputBg.SetColor(Color.ParseColor("#1AFFFFFF"));
        inputBg.SetStroke(1, Color.ParseColor("#30FFFFFF"));

        var wrapper = new FrameLayout(Context!);
        wrapper.Background = inputBg;
        wrapper.SetPadding(_dp * 8, _dp * 4, _dp * 8, _dp * 4);
        wrapper.AddView(_inputField);

        container.AddView(wrapper);
        _cardLayout.AddView(container);
        return this;
    }

    public GlassDialog AddItem(string text, Action onClick)
    {
        EnsureItemsContainer();

        var itemLayout = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        itemLayout.SetGravity(GravityFlags.CenterVertical);
        itemLayout.SetPadding(_dp * 8, _dp * 6, _dp * 8, _dp * 6);
        itemLayout.SetBackgroundColor(Color.Transparent);

        var tv = new TextView(Context!) { Text = text };
        tv.SetTextSize(ComplexUnitType.Sp, 14f);
        tv.SetTextColor(Color.White);
        tv.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        itemLayout.AddView(tv);

        itemLayout.Clickable = true;
        itemLayout.Focusable = true;

        var pressedColor = WithAlpha(_themeColor, 0x1A).ToArgb();
        var normalColor = Color.Transparent.ToArgb();
        var stateList = new Android.Content.Res.ColorStateList(
            new[] { new[] { Android.Resource.Attribute.StatePressed }, new int[] { } },
            new[] { pressedColor, normalColor }
        );
        var ripple = new RippleDrawable(stateList,
            null, new ShapeDrawable(new RoundRectShape(
                Enumerable.Repeat(12f * _density, 8).ToArray(), null, null)));
        itemLayout.Background = ripple;

        var capturedAction = onClick;
        itemLayout.Click += (s, e) =>
        {
            capturedAction();
            Dismiss();
        };

        _itemsContainer!.AddView(itemLayout);
        return this;
    }

    public GlassDialog AddDivider()
    {
        var divider = new View(Context!);
        divider.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1);
        divider.SetBackgroundColor(Color.ParseColor("#20FFFFFF"));
        _cardLayout.AddView(divider);
        return this;
    }

    public GlassDialog AddMessage(string message)
    {
        var tv = new TextView(Context!) { Text = message };
        tv.SetTextSize(ComplexUnitType.Sp, 13f);
        tv.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        tv.SetPadding(_dp * 14, _dp * 10, _dp * 14, _dp * 10);
        _cardLayout.AddView(tv);
        return this;
    }

    public GlassDialog AddPositiveButton(string text, Action<string?> onClick)
    {
        EnsureItemsContainer();

        AddDivider();

        var btnRow = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        btnRow.SetGravity(GravityFlags.End);
        btnRow.SetPadding(_dp * 8, _dp * 8, _dp * 8, _dp * 4);

        var btn = new TextView(Context!) { Text = text };
        btn.SetTextSize(ComplexUnitType.Sp, 13f);
        btn.SetTextColor(_themeColor);
        btn.SetTypeface(null, TypefaceStyle.Bold);
        btn.SetPadding(_dp * 20, _dp * 8, _dp * 20, _dp * 8);
        btn.Gravity = GravityFlags.Center;

        var btnBg = new GradientDrawable();
        btnBg.SetShape(ShapeType.Rectangle);
        btnBg.SetCornerRadius(16 * _density);
        btnBg.SetColor(WithAlpha(_themeColor, 0x1A));
        btn.Background = btnBg;
        btn.Clickable = true;
        btn.Focusable = true;

        btn.Click += (s, e) =>
        {
            onClick(InputText);
            Dismiss();
        };

        btnRow.AddView(btn);
        _itemsContainer!.AddView(btnRow);
        return this;
    }

    public GlassDialog AddNegativeButton(string text, Action? onClick = null)
    {
        EnsureItemsContainer();

        var btnRow = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        btnRow.SetGravity(GravityFlags.End);
        btnRow.SetPadding(_dp * 8, _dp * 4, _dp * 8, _dp * 4);

        var btn = new TextView(Context!) { Text = text };
        btn.SetTextSize(ComplexUnitType.Sp, 13f);
        btn.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        btn.SetPadding(_dp * 20, _dp * 8, _dp * 20, _dp * 8);
        btn.Gravity = GravityFlags.Center;
        btn.Clickable = true;
        btn.Focusable = true;

        btn.Click += (s, e) =>
        {
            onClick?.Invoke();
            Dismiss();
        };

        btnRow.AddView(btn);
        _itemsContainer!.AddView(btnRow);
        return this;
    }

    public GlassDialog AddCustomView(View view)
    {
        _cardLayout.AddView(view);
        return this;
    }

    private void EnsureItemsContainer()
    {
        if (_itemsContainer != null) return;
        _itemsContainer = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        _cardLayout.AddView(_itemsContainer);
    }

    public new GlassDialog Show()
    {
        SetContentView(_cardLayout);
        SetCanceledOnTouchOutside(true);
        Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        Window?.SetDimAmount(0.55f);
        Window?.SetGravity(GravityFlags.Center);
        Window?.SetLayout(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            try
            {
                Window?.SetBackgroundBlurRadius(300);
            }
            catch { }
        }
        else
        {
            ApplyPreSBlur();
        }

        base.Show();
        return this;
    }

    private void ApplyPreSBlur()
    {
        try
        {
            var activity = Context as Android.App.Activity;
            if (activity?.Window?.DecorView == null) return;

            var decorView = activity.Window.DecorView;
            decorView.DrawingCacheEnabled = true;
            decorView.BuildDrawingCache();
            var bitmap = decorView.DrawingCache;

            if (bitmap == null) return;

            var scaled = Bitmap.CreateScaledBitmap(bitmap,
                bitmap.Width / 8, bitmap.Height / 8, false);

            /* 优先使用 C++ Stack Blur（比 RenderScript 更快且无 API 版本限制） */
            Bitmap? blurred = null;
            if (NativeInterop.IsAvailable)
            {
                try { blurred = ApplyStackBlur(scaled, 15); }
                catch { }
            }
            /* 回退到 RenderScript */
            blurred ??= ApplyBlur(scaled, 15);

            var drawable = new BitmapDrawable(Context?.Resources, blurred);
            drawable.SetAlpha(180);
            Window?.SetBackgroundDrawable(drawable);

            scaled?.Recycle();
        }
        catch { }
    }

    /// <summary>
    /// 使用 C++ Stack Blur 模糊位图
    /// 将像素数据传入原生库处理，无需 RenderScript 上下文
    /// </summary>
    private static Bitmap ApplyStackBlur(Bitmap src, int radius)
    {
        var pixels = new int[src.Width * src.Height];
        src.GetPixels(pixels, 0, src.Width, 0, 0, src.Width, src.Height);

        /* 转换为 uint[] 传给原生库 */
        var uintPixels = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            uintPixels[i] = (uint)pixels[i];

        NativeInterop.StackBlurArgb(uintPixels, src.Width, src.Height, radius);

        /* 写回 Bitmap */
        for (int i = 0; i < uintPixels.Length; i++)
            pixels[i] = (int)uintPixels[i];
        src.SetPixels(pixels, 0, src.Width, 0, 0, src.Width, src.Height);
        return src;
    }

    private static Bitmap ApplyBlur(Bitmap src, int radius)
    {
        var rs = Android.Renderscripts.RenderScript.Create(
            global::Android.App.Application.Context);
        var input = Android.Renderscripts.Allocation.CreateFromBitmap(rs, src);
        var output = Android.Renderscripts.Allocation.CreateTyped(rs, input.Type);
        var script = Android.Renderscripts.ScriptIntrinsicBlur.Create(rs, input.Element);
        script.SetRadius(Math.Min(radius, 25));
        script.SetInput(input);
        script.ForEach(output);
        output.CopyTo(src);
        rs.Destroy();
        return src;
    }
}
