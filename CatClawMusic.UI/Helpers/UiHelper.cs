using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>UI 辅助工具类，提供按压缩放动画、颜色状态列表和主题色解析等常用功能</summary>
public static class UiHelper
{
    /// <summary>从当前主题中解析指定属性的颜色值，解析失败时返回回退色</summary>
    /// <param name="ctx">Android 上下文</param>
    /// <param name="attrId">主题属性 ID（如 Resource.Attribute.catClawPrimaryColor）</param>
    /// <param name="fallback">解析失败时的回退颜色值</param>
    /// <returns>解析到的 ARGB 颜色值</returns>
    public static int ResolveThemeColor(Context ctx, int attrId, int fallback = 0)
    {
        var tv = new TypedValue();
        return ctx.Theme?.ResolveAttribute(attrId, tv, true) == true
            ? tv.Data : fallback;
    }

    /// <summary>从当前主题中解析颜色并以 #RRGGBB 格式返回（不含 Alpha 通道）</summary>
    /// <param name="ctx">Android 上下文</param>
    /// <param name="attrId">主题属性 ID</param>
    /// <param name="fallbackHex">解析失败时的回退十六进制颜色字符串</param>
    /// <returns>#RRGGBB 格式的颜色字符串</returns>
    public static string ResolveThemeColorHex(Context ctx, int attrId, string fallbackHex = "#000000")
    {
        var tv = new TypedValue();
        if (ctx.Theme?.ResolveAttribute(attrId, tv, true) != true)
            return fallbackHex;
        var c = new Color(tv.Data);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
    /// <summary>为 View 添加按压缩放动画效果</summary>
    public static void ApplyPressScale(View view, float scale = 0.96f)
    {
        view.Touch += (s, e) =>
        {
            switch (e?.Event?.Action)
            {
                case MotionEventActions.Down:
                    view.Animate()?.ScaleX(scale)?.ScaleY(scale)?.SetDuration(100)?.Start();
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    view.Animate()?.ScaleX(1f)?.ScaleY(1f)?.SetDuration(100)?.Start();
                    break;
            }
        };
    }

    /// <summary>创建 ColorStateList，支持正常/按下两种状态</summary>
    public static ColorStateList CreateColorStateList(Context context, int normalColorRes, int pressedColorRes)
    {
        return new ColorStateList(
            new[] { new[] { global::Android.Resource.Attribute.StatePressed }, new int[0] },
            new[] { context.GetColor(pressedColorRes), context.GetColor(normalColorRes) });
    }
}
