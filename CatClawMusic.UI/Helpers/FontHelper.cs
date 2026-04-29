using Android.Content;
using Android.Graphics;

namespace CatClawMusic.UI.Helpers;

public static class FontHelper
{
    public static Typeface? LoadFont(Context context, string fontName)
    {
        int resId = fontName switch
        {
            "happy_zcool_2016" => Resource.Font.happy_zcool_2016,
            _ => 0
        };
        return resId != 0 ? AndroidX.Core.Content.Resources.ResourcesCompat.GetFont(context, resId) : null;
    }
}
