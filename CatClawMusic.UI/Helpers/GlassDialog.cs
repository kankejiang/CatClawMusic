using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Graphics.Drawables.Shapes;
using Android.Views;
using Android.Widget;
using System.Linq;
using Android.Util;
using Android.OS;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Services;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 毛玻璃风格对话框，支持标题、输入框、列表项、按钮等组件的链式构建
/// <para>Android 12+ 使用系统 SetBackgroundBlurRadius 实现原生模糊；低版本回退到 StackBlur/RenderScript</para>
/// <para>所有 AddXxx 方法返回 this，支持链式调用：dialog.SetTitle(...).AddItem(...).Show()</para>
/// </summary>
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

        // 从应用自定义主题属性读取主题色
        var tv = new TypedValue();
        _themeColor = ctx.Theme?.ResolveAttribute(Resource.Attribute.catClawPrimaryColor, tv, true) == true
            ? new Color(tv.Data)
            : Color.ParseColor("#9B7ED8");

        RequestWindowFeature((int)WindowFeatures.NoTitle);

        _cardLayout = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        _cardLayout.SetPadding(_dp * 6, _dp * 8, _dp * 6, _dp * 8);

        // 毛玻璃背景：深色底 + 主题色微染，让模糊效果透出同时保持可读性
        var bgBase = new GradientDrawable();
        bgBase.SetShape(ShapeType.Rectangle);
        bgBase.SetCornerRadius(24 * _density);
        bgBase.SetColor(Color.ParseColor("#E60D0D18")); // 深色底 (~90% 不透明度)
        bgBase.SetStroke(_dp, Color.ParseColor("#30FFFFFF")); // 亮色边框增强玻璃质感

        var bgTint = new GradientDrawable();
        bgTint.SetShape(ShapeType.Rectangle);
        bgTint.SetCornerRadius(24 * _density);
        bgTint.SetColor(WithAlpha(_themeColor, 0x24)); // 主题色微染 (~14% 不透明度)

        _cardLayout.Background = new LayerDrawable(new Drawable[] { bgBase, bgTint });
    }

    private static Color WithAlpha(Color c, int alpha)
    {
        return new Color((byte)(c.ToArgb() >> 16 & 0xFF),
            (byte)(c.ToArgb() >> 8 & 0xFF),
            (byte)(c.ToArgb() & 0xFF),
            (byte)alpha);
    }

    /// <summary>设置对话框标题和可选副标题</summary>
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

    /// <summary>添加文本输入框</summary>
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

    /// <summary>添加可点击列表项，点击后自动关闭对话框</summary>
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

    /// <summary>添加带高亮样式的列表项（如当前选中项）</summary>
    public GlassDialog AddItemWithHighlight(string text, bool isHighlighted, Action onClick)
    {
        EnsureItemsContainer();

        var itemLayout = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        itemLayout.SetGravity(GravityFlags.CenterVertical);
        itemLayout.SetPadding(_dp * 8, _dp * 6, _dp * 8, _dp * 6);
        itemLayout.SetBackgroundColor(Color.Transparent);

        var tv = new TextView(Context!) { Text = text };
        tv.SetTextSize(ComplexUnitType.Sp, 14f);
        tv.SetTextColor(isHighlighted ? _themeColor : Color.White);
        tv.SetTypeface(null, isHighlighted ? TypefaceStyle.Bold : TypefaceStyle.Normal);
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

    /// <summary>添加分割线</summary>
    public GlassDialog AddDivider()
    {
        var divider = new View(Context!);
        divider.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1);
        divider.SetBackgroundColor(Color.ParseColor("#20FFFFFF"));
        _cardLayout.AddView(divider);
        return this;
    }

    /// <summary>添加消息文本</summary>
    public GlassDialog AddMessage(string message)
    {
        var tv = new TextView(Context!) { Text = message };
        tv.SetTextSize(ComplexUnitType.Sp, 13f);
        tv.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        tv.SetPadding(_dp * 14, _dp * 10, _dp * 14, _dp * 10);
        _cardLayout.AddView(tv);
        return this;
    }

    /// <summary>添加确认按钮，点击时回调传入输入框文本</summary>
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

    /// <summary>添加取消按钮</summary>
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

    /// <summary>添加自定义 View</summary>
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

    /// <summary>显示对话框，配置毛玻璃背景模糊效果</summary>
    public new GlassDialog Show()
    {
        SetContentView(_cardLayout);
        SetCanceledOnTouchOutside(true);
        Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        Window?.SetDimAmount(0.3f); // 降低遮罩透明度，让模糊效果透出来
        Window?.SetGravity(GravityFlags.Center);
        Window?.SetLayout(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            try
            {
                // 启用窗口后方模糊 + 大半径模糊
                Window?.AddFlags(WindowManagerFlags.BlurBehind);
                Window?.SetBackgroundBlurRadius(500);
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

    /// <summary>Android 12 以下版本的模糊回退：截屏 → 缩小 → StackBlur/RenderScript 模糊 → 设为背景</summary>
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

            /* 使用纯 C# Stack Blur（NativeAOT 兼容，无 P/Invoke 依赖） */
            Bitmap? blurred = ApplyStackBlurCSharp(scaled, 15);
            /* 回退到 RenderScript */
            blurred ??= ApplyBlur(scaled, 15);

            var drawable = new BitmapDrawable(Context?.Resources, blurred);
            drawable.SetAlpha(180);
            Window?.SetBackgroundDrawable(drawable);

            scaled?.Recycle();
        }
        catch { }
    }

    /// <summary>纯 C# Stack Blur 实现（NativeAOT 兼容，无 P/Invoke 依赖）</summary>
    private static Bitmap ApplyStackBlurCSharp(Bitmap src, int radius)
    {
        int w = src.Width;
        int h = src.Height;
        int wh = w * h;
        int[] pixels = new int[wh];
        src.GetPixels(pixels, 0, w, 0, 0, w, h);

        byte[] r = new byte[wh];
        byte[] g = new byte[wh];
        byte[] b = new byte[wh];
        for (int i = 0; i < wh; i++)
        {
            int p = pixels[i];
            r[i] = (byte)((p >> 16) & 0xFF);
            g[i] = (byte)((p >> 8) & 0xFF);
            b[i] = (byte)(p & 0xFF);
        }

        int div = radius + radius + 1;
        byte[] rout = new byte[wh];
        byte[] gout = new byte[wh];
        byte[] bout = new byte[wh];

        // Horizontal pass
        for (int y = 0; y < h; y++)
        {
            int yw = y * w;
            int sr = 0, sg = 0, sb = 0;
            for (int i = -radius; i <= radius; i++)
            {
                int idx = yw + Math.Clamp(i, 0, w - 1);
                sr += r[idx]; sg += g[idx]; sb += b[idx];
            }
            for (int x = 0; x < w; x++)
            {
                int idx = yw + x;
                rout[idx] = (byte)(sr / div);
                gout[idx] = (byte)(sg / div);
                bout[idx] = (byte)(sb / div);
                int rmvIdx = yw + Math.Max(0, x - radius);
                int addIdx = yw + Math.Min(w - 1, x + radius + 1);
                sr += r[addIdx] - r[rmvIdx];
                sg += g[addIdx] - g[rmvIdx];
                sb += b[addIdx] - b[rmvIdx];
            }
        }

        // Vertical pass
        for (int x = 0; x < w; x++)
        {
            int sr = 0, sg = 0, sb = 0;
            for (int i = -radius; i <= radius; i++)
            {
                int idx = Math.Clamp(i, 0, h - 1) * w + x;
                sr += rout[idx]; sg += gout[idx]; sb += bout[idx];
            }
            for (int y = 0; y < h; y++)
            {
                int idx = y * w + x;
                pixels[idx] = unchecked((int)(0xFF000000u | ((uint)(
                    sb / div) | ((uint)(sg / div) << 8) | ((uint)(sr / div) << 16))));
                int rmvIdx = Math.Max(0, y - radius) * w + x;
                int addIdx = Math.Min(h - 1, y + radius + 1) * w + x;
                sr += rout[addIdx] - rout[rmvIdx];
                sg += gout[addIdx] - gout[rmvIdx];
                sb += bout[addIdx] - bout[rmvIdx];
            }
        }

        src.SetPixels(pixels, 0, w, 0, 0, w, h);
        return src;
    }

    /// <summary>RenderScript 模糊回退（API 17+，最大半径 25）</summary>
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
