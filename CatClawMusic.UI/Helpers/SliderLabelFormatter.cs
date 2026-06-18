using Google.Android.Material.Slider;

namespace CatClawMusic.UI.Helpers;

/// <summary>Material Slider 拖拽提示格式化器：将秒数格式化为 mm:ss</summary>
public class SliderLabelFormatter : Java.Lang.Object, ILabelFormatter
{
    public string? GetFormattedValue(float value)
    {
        var totalSeconds = (int)value;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }
}
