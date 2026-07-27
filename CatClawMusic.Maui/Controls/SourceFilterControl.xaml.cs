namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 全部 / 本地 / 网络 三态筛选控件。
/// 用于"全部音乐"页面内快速切换歌曲来源。
/// </summary>
public partial class SourceFilterControl : ContentView
{
    public static readonly BindableProperty SelectedModeProperty =
        BindableProperty.Create(nameof(SelectedMode), typeof(string), typeof(SourceFilterControl),
            defaultValue: "all",
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSelectedModeChanged);

    public static readonly BindableProperty ActiveColorProperty =
        BindableProperty.Create(nameof(ActiveColor), typeof(Color), typeof(SourceFilterControl),
            defaultValue: null,
            propertyChanged: OnColorsChanged);

    public static readonly BindableProperty InactiveTextColorProperty =
        BindableProperty.Create(nameof(InactiveTextColor), typeof(Color), typeof(SourceFilterControl),
            defaultValue: null,
            propertyChanged: OnColorsChanged);

    /// <summary>当前选中的模式：all / local / network。</summary>
    public string SelectedMode
    {
        get => (string)GetValue(SelectedModeProperty);
        set => SetValue(SelectedModeProperty, value);
    }

    /// <summary>选中按钮背景色，为 null 时回退到 PrimaryColor 资源。</summary>
    public Color? ActiveColor
    {
        get => (Color?)GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

    /// <summary>未选中文字颜色，为 null 时回退到 TextPrimaryColor 资源。</summary>
    public Color? InactiveTextColor
    {
        get => (Color?)GetValue(InactiveTextColorProperty);
        set => SetValue(InactiveTextColorProperty, value);
    }

    public SourceFilterControl()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    private static void OnSelectedModeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SourceFilterControl)bindable).UpdateVisualState();

    private static void OnColorsChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SourceFilterControl)bindable).UpdateVisualState();

    private void OnAllTapped(object? sender, EventArgs e)
    {
        if (SelectedMode != "all")
            SelectedMode = "all";
    }

    private void OnLocalTapped(object? sender, EventArgs e)
    {
        if (SelectedMode != "local")
            SelectedMode = "local";
    }

    private void OnNetworkTapped(object? sender, EventArgs e)
    {
        if (SelectedMode != "network")
            SelectedMode = "network";
    }

    private void UpdateVisualState()
    {
        var activeBg = ActiveColor ?? (Color?)(Application.Current?.Resources["PrimaryColor"]) ?? Color.FromArgb("#8C7BFF");
        var inactiveText = InactiveTextColor ?? (Color?)(Application.Current?.Resources["TextPrimaryColor"]) ?? Colors.White;
        var activeText = Colors.White;

        AllButton.BackgroundColor = SelectedMode == "all" ? activeBg : Colors.Transparent;
        LocalButton.BackgroundColor = SelectedMode == "local" ? activeBg : Colors.Transparent;
        NetworkButton.BackgroundColor = SelectedMode == "network" ? activeBg : Colors.Transparent;

        AllLabel.TextColor = SelectedMode == "all" ? activeText : inactiveText;
        LocalLabel.TextColor = SelectedMode == "local" ? activeText : inactiveText;
        NetworkLabel.TextColor = SelectedMode == "network" ? activeText : inactiveText;
    }
}
