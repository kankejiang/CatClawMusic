namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 网格/列表视图切换按钮组件。
/// 通过 <see cref="IsGridView"/> 双向绑定控制当前视图模式，点击内部自动切换。
/// 选中态背景色由 <see cref="ActiveColor"/> / <see cref="InactiveColor"/> 控制，
/// 默认使用主题色。
/// </summary>
public partial class ViewToggleButton : ContentView
{
    public static readonly BindableProperty IsGridViewProperty =
        BindableProperty.Create(nameof(IsGridView), typeof(bool), typeof(ViewToggleButton),
            defaultValue: true,
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnIsGridViewChanged);

    public static readonly BindableProperty ActiveColorProperty =
        BindableProperty.Create(nameof(ActiveColor), typeof(Color), typeof(ViewToggleButton),
            defaultValue: null,
            propertyChanged: OnColorsChanged);

    public static readonly BindableProperty InactiveColorProperty =
        BindableProperty.Create(nameof(InactiveColor), typeof(Color), typeof(ViewToggleButton),
            defaultValue: null,
            propertyChanged: OnColorsChanged);

    /// <summary>是否处于网格视图模式（双向绑定）。</summary>
    public bool IsGridView
    {
        get => (bool)GetValue(IsGridViewProperty);
        set => SetValue(IsGridViewProperty, value);
    }

    /// <summary>选中按钮背景色，为 null 时回退到 PrimaryColor 资源。</summary>
    public Color? ActiveColor
    {
        get => (Color?)GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

    /// <summary>未选中按钮背景色，为 null 时回退到 Transparent。</summary>
    public Color? InactiveColor
    {
        get => (Color?)GetValue(InactiveColorProperty);
        set => SetValue(InactiveColorProperty, value);
    }

    public ViewToggleButton()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    private static void OnIsGridViewChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ViewToggleButton)bindable).UpdateVisualState();

    private static void OnColorsChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ViewToggleButton)bindable).UpdateVisualState();

    private void OnGridTapped(object? sender, EventArgs e)
    {
        if (!IsGridView)
            IsGridView = true;
    }

    private void OnListTapped(object? sender, EventArgs e)
    {
        if (IsGridView)
            IsGridView = false;
    }

    private void UpdateVisualState()
    {
        var active = ActiveColor ?? (Color?)Application.Current?.Resources?["PrimaryColor"] ?? Color.FromArgb("#8C7BFF");
        var inactive = InactiveColor ?? Colors.Transparent;

        GridButton.BackgroundColor = IsGridView ? active : inactive;
        ListButton.BackgroundColor = IsGridView ? inactive : active;
    }
}
