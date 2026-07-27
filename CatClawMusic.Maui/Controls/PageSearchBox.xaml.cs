using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 可复用的页面搜索框组件。
/// 提供 Placeholder、SearchQuery 双向绑定、SearchCompleted 事件。
/// </summary>
public partial class PageSearchBox : ContentView
{
    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(PageSearchBox),
            defaultValue: string.Empty,
            propertyChanged: OnPlaceholderChanged);

    public static readonly BindableProperty SearchQueryProperty =
        BindableProperty.Create(nameof(SearchQuery), typeof(string), typeof(PageSearchBox),
            defaultValue: string.Empty,
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSearchQueryChanged);

    public static readonly BindableProperty SearchCompletedCommandProperty =
        BindableProperty.Create(nameof(SearchCompletedCommand), typeof(ICommand), typeof(PageSearchBox));

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(PageSearchBox),
            defaultValue: 14.0,
            propertyChanged: OnFontSizeChanged);

    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(double), typeof(PageSearchBox),
            defaultValue: 14.0,
            propertyChanged: OnCornerRadiusChanged);

    /// <summary>搜索框占位文字。</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>搜索文本（双向绑定）。</summary>
    public string SearchQuery
    {
        get => (string)GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    /// <summary>用户按下搜索键时执行的命令。</summary>
    public ICommand? SearchCompletedCommand
    {
        get => (ICommand?)GetValue(SearchCompletedCommandProperty);
        set => SetValue(SearchCompletedCommandProperty, value);
    }

    /// <summary>文字字号。</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>圆角半径。</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public PageSearchBox()
    {
        InitializeComponent();
        SearchEntry.Completed += OnEntryCompleted;
    }

    private static void OnPlaceholderChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PageSearchBox)bindable).SearchEntry.Placeholder = (string)newValue;

    private static void OnSearchQueryChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var ctrl = (PageSearchBox)bindable;
        if (ctrl.SearchEntry.Text != (string?)newValue)
            ctrl.SearchEntry.Text = (string?)newValue;
    }

    private static void OnFontSizeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PageSearchBox)bindable).SearchEntry.FontSize = (double)newValue;

    private static void OnCornerRadiusChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PageSearchBox)bindable).SearchBorder.StrokeShape = new RoundRectangle { CornerRadius = (double)newValue };

    private void OnEntryCompleted(object? sender, EventArgs e)
    {
        SearchQuery = SearchEntry.Text ?? string.Empty;
        if (SearchCompletedCommand?.CanExecute(SearchQuery) == true)
            SearchCompletedCommand.Execute(SearchQuery);
    }
}
