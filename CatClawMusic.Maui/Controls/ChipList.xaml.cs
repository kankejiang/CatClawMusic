using System.Collections;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 可复用的 chip 列表组件，用于渲染筛选/排序 chips。
/// 支持横向可滚动（Horizontal）和纵向堆叠（Vertical）两种布局。
/// chip 项需提供 <c>Label</c>、<c>BackgroundColor</c>、<c>BorderColor</c>、<c>TextColor</c> 属性（如 <see cref="ViewModels.FilterChip"/>/<see cref="ViewModels.SortOption"/>）。
/// </summary>
public partial class ChipList : ContentView
{
    /// <summary>chip 被点击时发生。事件参数为被点击项的 BindingContext。</summary>
    public event EventHandler<object?>? ChipTapped;

    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ChipList),
            propertyChanged: OnItemsSourceChanged);

    public static readonly BindableProperty OrientationProperty =
        BindableProperty.Create(nameof(Orientation), typeof(StackOrientation), typeof(ChipList),
            defaultValue: StackOrientation.Horizontal,
            propertyChanged: OnOrientationChanged);

    public static readonly BindableProperty SpacingProperty =
        BindableProperty.Create(nameof(Spacing), typeof(double), typeof(ChipList),
            defaultValue: 8.0,
            propertyChanged: OnSpacingChanged);

    public static readonly BindableProperty ChipPaddingProperty =
        BindableProperty.Create(nameof(ChipPadding), typeof(Thickness), typeof(ChipList),
            defaultValue: new Thickness(14, 7));

    public static readonly BindableProperty ChipCornerRadiusProperty =
        BindableProperty.Create(nameof(ChipCornerRadius), typeof(double), typeof(ChipList),
            defaultValue: 999.0);

    public static readonly BindableProperty ChipFontSizeProperty =
        BindableProperty.Create(nameof(ChipFontSize), typeof(double), typeof(ChipList),
            defaultValue: 12.5);

    public static readonly BindableProperty ChipTappedCommandProperty =
        BindableProperty.Create(nameof(ChipTappedCommand), typeof(ICommand), typeof(ChipList));

    /// <summary>chip 数据源（FilterChip/SortOption 等）。</summary>
    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>布局方向：Horizontal 为横向可滚动，Vertical 为纵向堆叠。</summary>
    public StackOrientation Orientation
    {
        get => (StackOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>chip 之间的间距。</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>单个 chip 的内边距。</summary>
    public Thickness ChipPadding
    {
        get => (Thickness)GetValue(ChipPaddingProperty);
        set => SetValue(ChipPaddingProperty, value);
    }

    /// <summary>chip 的圆角半径。</summary>
    public double ChipCornerRadius
    {
        get => (double)GetValue(ChipCornerRadiusProperty);
        set => SetValue(ChipCornerRadiusProperty, value);
    }

    /// <summary>chip 文字字号。</summary>
    public double ChipFontSize
    {
        get => (double)GetValue(ChipFontSizeProperty);
        set => SetValue(ChipFontSizeProperty, value);
    }

    /// <summary>chip 被点击时执行的命令，参数为被点击项。</summary>
    public ICommand? ChipTappedCommand
    {
        get => (ICommand?)GetValue(ChipTappedCommandProperty);
        set => SetValue(ChipTappedCommandProperty, value);
    }

    public ChipList()
    {
        InitializeComponent();
        // 构造函数中立即应用初始方向：XAML 中 StackLayout 默认是 Vertical，
        // 若 BindableProperty 默认值为 Horizontal 且 XAML 未显式修改，
        // OnOrientationChanged 不会触发，导致视觉上仍是纵向排列。
        ApplyOrientation();
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ChipList)bindable).RebuildItems();

    private static void OnOrientationChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ChipList)bindable).ApplyOrientation();

    private static void OnSpacingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var ctrl = (ChipList)bindable;
        ctrl.ItemsHost.Spacing = (double)newValue;
    }

    /// <summary>子类可覆盖以自定义 chip 的内容构建。默认构建 Border+Label。</summary>
    protected virtual View BuildChip(object item)
    {
        var cornerRadius = ChipCornerRadius;
        var padding = ChipPadding;
        var fontSize = ChipFontSize;

        var border = new Border
        {
            Padding = padding,
            StrokeShape = new RoundRectangle { CornerRadius = cornerRadius },
            StrokeThickness = 1,
            BindingContext = item,
        };
        border.SetBinding(Border.BackgroundColorProperty, "BackgroundColor");
        border.SetBinding(Border.StrokeProperty, "BorderColor");

        var gesture = new TapGestureRecognizer();
        gesture.Tapped += OnChipTappedInternal;
        border.GestureRecognizers.Add(gesture);

        var label = new Label { FontSize = fontSize };
        label.SetBinding(Label.TextProperty, "Label");
        label.SetBinding(Label.TextColorProperty, "TextColor");
        border.Content = label;

        return border;
    }

    private void OnChipTappedInternal(object? sender, EventArgs e)
    {
        if (sender is View view && view.BindingContext is { } item)
        {
            ChipTapped?.Invoke(this, item);
            if (ChipTappedCommand?.CanExecute(item) == true)
                ChipTappedCommand.Execute(item);
        }
    }

    private void ApplyOrientation()
    {
        if (Orientation == StackOrientation.Horizontal)
        {
            ScrollHost.Orientation = ScrollOrientation.Horizontal;
            ItemsHost.Orientation = StackOrientation.Horizontal;
        }
        else
        {
            // 纵向模式不需要 ScrollView 包装
            ScrollHost.Orientation = ScrollOrientation.Vertical;
            ItemsHost.Orientation = StackOrientation.Vertical;
        }
    }

    private void RebuildItems()
    {
        ItemsHost.Children.Clear();
        if (ItemsSource is null) return;

        foreach (var item in ItemsSource)
            ItemsHost.Children.Add(BuildChip(item));
    }
}
