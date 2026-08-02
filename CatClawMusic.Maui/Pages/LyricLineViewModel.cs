using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// Windows 歌词列表项的 ViewModel（CollectionView ItemsSource 用）。
///
/// 设计原则（2026-08-02 重写）：
/// 1. 视图层只用原生 MAUI <see cref="Label"/>，不再用 KaraokeLabel。
///    Windows 的 KaraokeLabelHandler 只是"换颜色"的假回退实现（不画描边），
///    却在 FillProgress&lt;0.5 时偷偷把前景切成 OutlineColor 并乘 0.45 alpha，
///    导致非当前行整行隐形——这是"歌词只显示两行"的真正根因。
/// 2. 一行文字的可见性只由 TextColor + Opacity 两个属性决定，没有任何隐藏分支。
/// 3. 所有 tier 颜色 alpha 均 ≥ 0.38，任何一行都不可能透明消失。
/// 4. **所有 setter 必须做值比较后再通知**：高亮切换时会遍历刷新全部行，
///    若无条件触发 PropertyChanged，CollectionView（MeasureAllItems）会重测量所有项，
///    正在进行的 ScrollTo 会被布局刷新打断 → 表现为"歌词不滚动"。
/// </summary>
public class LyricLineViewModel : INotifyPropertyChanged, Microsoft.Maui.Controls.IAnimatable
{
    private string _text = "";
    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; OnPropertyChanged(); }
    }

    private string _translation = "";
    public string Translation
    {
        get => _translation;
        set
        {
            if (_translation == value) return;
            _translation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTranslation));
        }
    }

    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);

    private bool _isCurrent;
    /// <summary>是否为当前播放行（驱动左侧发光指示条的显隐）。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (_isCurrent == value) return; _isCurrent = value; OnPropertyChanged(); }
    }

    private double _mainFontSize = 18;
    public double MainFontSize
    {
        get => _mainFontSize;
        set { if (NearlyEqual(_mainFontSize, value)) return; _mainFontSize = value; OnPropertyChanged(); }
    }

    private double _transFontSize = 14;
    public double TransFontSize
    {
        get => _transFontSize;
        set { if (NearlyEqual(_transFontSize, value)) return; _transFontSize = value; OnPropertyChanged(); }
    }

    private FontAttributes _mainFontAttributes = FontAttributes.Bold;
    /// <summary>所有行均加粗（2026-08-02 用户要求：未唱行也加粗，层次交给字号/颜色/模糊表达）。</summary>
    public FontAttributes MainFontAttributes
    {
        get => _mainFontAttributes;
        set { if (_mainFontAttributes == value) return; _mainFontAttributes = value; OnPropertyChanged(); }
    }

    private double _blur;
    /// <summary>行的高斯模糊半径（DP）。当前行=0（清晰），离当前行越远越大，营造景深。</summary>
    public double Blur
    {
        get => _blur;
        set { if (NearlyEqual(_blur, value)) return; _blur = value; OnPropertyChanged(); }
    }

    private Thickness _rowPadding = new Thickness(0, 7, 0, 7);
    /// <summary>行上下内边距（DP）。当前行及其上下相邻行的间距被特意加大，让关键三行更透气。</summary>
    public Thickness RowPadding
    {
        get => _rowPadding;
        set { if (SameThickness(_rowPadding, value)) return; _rowPadding = value; OnPropertyChanged(); }
    }

    private Color _mainColor = Colors.White;
    public Color MainColor
    {
        get => _mainColor;
        set { if (SameColor(_mainColor, value)) return; _mainColor = value; OnPropertyChanged(); }
    }

    private Color _transColor = Colors.White;
    public Color TransColor
    {
        get => _transColor;
        set { if (SameColor(_transColor, value)) return; _transColor = value; OnPropertyChanged(); }
    }

    private double _mainOpacity = 1.0;
    public double MainOpacity
    {
        get => _mainOpacity;
        set { if (NearlyEqual(_mainOpacity, value)) return; _mainOpacity = value; OnPropertyChanged(); }
    }

    private double _transOpacity = 1.0;
    public double TransOpacity
    {
        get => _transOpacity;
        set { if (NearlyEqual(_transOpacity, value)) return; _transOpacity = value; OnPropertyChanged(); }
    }

    internal static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.01;

    private static bool SameThickness(Thickness a, Thickness b)
        => NearlyEqual(a.Left, b.Left) && NearlyEqual(a.Top, b.Top)
        && NearlyEqual(a.Right, b.Right) && NearlyEqual(a.Bottom, b.Bottom);

    internal static bool SameColor(Color? a, Color? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Red - b.Red) < 0.004f
            && Math.Abs(a.Green - b.Green) < 0.004f
            && Math.Abs(a.Blue - b.Blue) < 0.004f
            && Math.Abs(a.Alpha - b.Alpha) < 0.004f;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // IAnimatable 实现（Animation.Commit 需要；ViewModel 无需真正 batch，空实现即可）
    public void BatchBegin() { }
    public void BatchCommit() { }
}
