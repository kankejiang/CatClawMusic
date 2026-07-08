using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 全局应用弹窗控件：提供与应用风格一致的居中模态弹窗。
/// 用法：设置 Title、Content（或直接添加子元素到 PopupContent），
/// 调用 ShowAsync(page) 显示，点击遮罩或调用 Close() 关闭。
/// 支持自定义标题栏、关闭按钮和内容区域。
/// </summary>
public partial class AppPopup : ContentView
{
    /// <summary>弹窗标题文本</summary>
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(AppPopup), string.Empty,
            propertyChanged: OnTitleChanged);

    /// <summary>是否显示关闭按钮</summary>
    public static readonly BindableProperty ShowCloseButtonProperty =
        BindableProperty.Create(nameof(ShowCloseButton), typeof(bool), typeof(AppPopup), true);

    /// <summary>是否在点击遮罩时关闭</summary>
    public static readonly BindableProperty CloseOnMaskTappedProperty =
        BindableProperty.Create(nameof(CloseOnMaskTapped), typeof(bool), typeof(AppPopup), true);

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool ShowCloseButton { get => (bool)GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }
    public bool CloseOnMaskTapped { get => (bool)GetValue(CloseOnMaskTappedProperty); set => SetValue(CloseOnMaskTappedProperty, value); }

    /// <summary>弹窗关闭事件</summary>
    public event EventHandler? Closed;

    private Grid? _host;
    private View? _titleBar;
    private TaskCompletionSource<bool>? _showTcs;

    public AppPopup()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AppPopup popup)
            popup.RebuildTitleBar();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        RebuildTitleBar();
    }

    /// <summary>重建标题栏（标题 + 关闭按钮）</summary>
    private void RebuildTitleBar()
    {
        if (PopupContent == null) return;

        // 移除旧的标题栏
        if (_titleBar != null && PopupContent.Children.Contains(_titleBar))
            PopupContent.Children.Remove(_titleBar);

        if (string.IsNullOrEmpty(Title) && !ShowCloseButton)
            return;

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            },
            Margin = new Thickness(0, 0, 0, 16)
        };

        if (!string.IsNullOrEmpty(Title))
        {
            var titleLabel = new Label
            {
                Text = Title,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                VerticalOptions = LayoutOptions.Center
            };
            titleRow.Add(titleLabel, 0);
        }

        if (ShowCloseButton)
        {
            var closeBtn = new Border
            {
                BackgroundColor = (Color)Application.Current!.Resources["ChipInactiveColor"],
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
                StrokeThickness = 0,
                WidthRequest = 32,
                HeightRequest = 32,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(0),
                Content = new Label
                {
                    Text = "\u2715",
                    FontSize = 16,
                    TextColor = (Color)Application.Current.Resources["TextSecondaryColor"],
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Close();
            closeBtn.GestureRecognizers.Add(tap);
            titleRow.Add(closeBtn, 1);
        }

        _titleBar = titleRow;
        PopupContent.Children.Insert(0, _titleBar);
    }

    /// <summary>添加内容到弹窗主体</summary>
    public void AddContent(View view)
    {
        PopupContent.Children.Add(view);
    }

    /// <summary>清空弹窗内容（保留标题栏）</summary>
    public void ClearContent()
    {
        // 保留标题栏（索引0），移除其他
        var toRemove = PopupContent.Children.Skip(1).ToList();
        foreach (var child in toRemove)
            PopupContent.Children.Remove(child);
    }

    /// <summary>显示弹窗到指定页面的顶层 Grid</summary>
    public Task<bool> ShowAsync(ContentPage page)
    {
        _showTcs = new TaskCompletionSource<bool>();

        var pageContent = page.Content;
        if (pageContent is Grid grid)
        {
            _host = grid;
            grid.Children.Add(this);
            Grid.SetRowSpan(this, grid.RowDefinitions.Count > 0 ? grid.RowDefinitions.Count : 1);
            Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count > 0 ? grid.ColumnDefinitions.Count : 1);
        }
        else
        {
            // 非 Grid 顶层：用 Grid 包裹原内容
            var wrapper = new Grid();
            var parent = pageContent?.Parent as Layout;
            if (parent != null && pageContent != null)
            {
                var idx = parent.Children.IndexOf(pageContent);
                parent.Children.Remove(pageContent);
                wrapper.Children.Add(pageContent);
                wrapper.Children.Add(this);
                parent.Children.Insert(idx, wrapper);
            }
            _host = wrapper;
        }

        // 入场动画
        this.Opacity = 0;
        this.Scale = 0.9;
        this.FadeTo(1, 220, Easing.CubicOut);
        PopupCard.FadeTo(1, 220, Easing.CubicOut);
        PopupCard.TranslateTo(0, 0, 280, Easing.CubicOut);
        this.ScaleTo(1, 220, Easing.CubicOut);

        return _showTcs.Task;
    }

    /// <summary>关闭弹窗</summary>
    public async void Close()
    {
        if (_host == null) return;

        // 出场动画
        await Task.WhenAll(
            this.FadeTo(0, 180, Easing.CubicIn),
            PopupCard.TranslateTo(0, 20, 180, Easing.CubicIn),
            PopupCard.FadeTo(0, 180, Easing.CubicIn)
        );

        _host.Children.Remove(this);
        _host = null;
        Closed?.Invoke(this, EventArgs.Empty);
        _showTcs?.TrySetResult(true);
    }

    private void OnMaskTapped(object sender, EventArgs e)
    {
        if (CloseOnMaskTapped)
            Close();
    }
}
