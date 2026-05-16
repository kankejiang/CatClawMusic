using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>UI 辅助工具类，提供按压缩放动画和颜色状态列表等常用功能</summary>
public static class UiHelper
{
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
