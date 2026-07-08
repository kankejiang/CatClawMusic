using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 卡拉OK式歌词标签：支持空心描边（未唱）与实心填充（已唱）切换，
/// 并为后期逐字渐进填充预留 FillProgress（0~1）属性。
/// Android 平台用 Canvas + Paint.Style.Stroke/Fill 绘制；
/// 其他平台回退为普通 Label 行为。
/// </summary>
public class KaraokeLabel : View
{
    /// <summary>歌词文本</summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(KaraokeLabel), string.Empty,
            propertyChanged: OnVisualPropChanged);

    /// <summary>字号</summary>
    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(KaraokeLabel), 16.0,
            propertyChanged: OnVisualPropChanged);

    /// <summary>字体族</summary>
    public static readonly BindableProperty FontFamilyProperty =
        BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(KaraokeLabel), "OpenSansSemibold",
            propertyChanged: OnVisualPropChanged);

    /// <summary>字体属性（Bold 等）</summary>
    public static readonly BindableProperty FontAttributesProperty =
        BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(KaraokeLabel), FontAttributes.None,
            propertyChanged: OnVisualPropChanged);

    /// <summary>实心填充颜色（已唱部分）</summary>
    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(KaraokeLabel), Colors.White,
            propertyChanged: OnVisualPropChanged);

    /// <summary>空心描边颜色（未唱部分）</summary>
    public static readonly BindableProperty OutlineColorProperty =
        BindableProperty.Create(nameof(OutlineColor), typeof(Color), typeof(KaraokeLabel), Colors.White,
            propertyChanged: OnVisualPropChanged);

    /// <summary>描边宽度（像素）</summary>
    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(KaraokeLabel), 2.0,
            propertyChanged: OnVisualPropChanged);

    /// <summary>
    /// 填充进度：0=全空心（未唱），1=全实心（已唱）。
    /// 后期逐字歌词时，每行可按字符进度平滑过渡 0→1。
    /// </summary>
    public static readonly BindableProperty FillProgressProperty =
        BindableProperty.Create(nameof(FillProgress), typeof(double), typeof(KaraokeLabel), 1.0,
            propertyChanged: OnVisualPropChanged);

    /// <summary>水平对齐</summary>
    public static readonly BindableProperty HorizontalTextAlignmentProperty =
        BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(KaraokeLabel), TextAlignment.Center,
            propertyChanged: OnVisualPropChanged);

    /// <summary>换行模式</summary>
    public static readonly BindableProperty LineBreakModeProperty =
        BindableProperty.Create(nameof(LineBreakMode), typeof(LineBreakMode), typeof(KaraokeLabel), LineBreakMode.WordWrap,
            propertyChanged: OnVisualPropChanged);

    /// <summary>内边距</summary>
    public static readonly BindableProperty PaddingProperty =
        BindableProperty.Create(nameof(Padding), typeof(Thickness), typeof(KaraokeLabel), default(Thickness),
            propertyChanged: OnVisualPropChanged);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public string FontFamily { get => (string)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty); set => SetValue(FontAttributesProperty, value); }
    public Color TextColor { get => (Color)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public Color OutlineColor { get => (Color)GetValue(OutlineColorProperty); set => SetValue(OutlineColorProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public double FillProgress { get => (double)GetValue(FillProgressProperty); set => SetValue(FillProgressProperty, value); }
    public TextAlignment HorizontalTextAlignment { get => (TextAlignment)GetValue(HorizontalTextAlignmentProperty); set => SetValue(HorizontalTextAlignmentProperty, value); }
    public LineBreakMode LineBreakMode { get => (LineBreakMode)GetValue(LineBreakModeProperty); set => SetValue(LineBreakModeProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    private static void OnVisualPropChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is KaraokeLabel label)
            label.Handler?.UpdateValue(nameof(KaraokeLabel));
    }
}
