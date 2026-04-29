using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

public static class UiHelper
{
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

    public static ColorStateList CreateColorStateList(Context context, int normalColorRes, int pressedColorRes)
    {
        return new ColorStateList(
            new[] { new[] { global::Android.Resource.Attribute.StatePressed }, new int[0] },
            new[] { context.GetColor(pressedColorRes), context.GetColor(normalColorRes) });
    }
}
