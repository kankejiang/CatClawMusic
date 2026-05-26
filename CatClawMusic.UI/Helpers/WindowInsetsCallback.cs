using Android.Views;
using AndroidX.Core.View;

namespace CatClawMusic.UI.Helpers;

public class WindowInsetsCallback : Java.Lang.Object, IOnApplyWindowInsetsListener
{
    private readonly Func<View, WindowInsetsCompat, WindowInsetsCompat> _handler;

    public WindowInsetsCallback(Func<View, WindowInsetsCompat, WindowInsetsCompat> handler)
    {
        _handler = handler;
    }

    public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
    {
        return _handler(v, insets);
    }
}
