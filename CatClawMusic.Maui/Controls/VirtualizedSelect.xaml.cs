using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 虚拟化选择器条目。
/// Icon 为左侧圆形中的字形/emoji；Text 为主文案；Subtitle 可选副文案；
/// TrailingIcon 为右侧尾标（如 "›" 表示可进入子列表，"✓" 表示已选）；
/// OnSelected 为点击回调；KeepOpen=true 时点击后不自动收起（用于进入子列表/结果态）。
/// </summary>
public class VirtualizedSelectItem
{
    public string? Icon { get; set; }
    public string Text { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? TrailingIcon { get; set; }
    public object? Tag { get; set; }
    public Action<VirtualizedSelectItem>? OnSelected { get; set; }
    public bool KeepOpen { get; set; }
}

/// <summary>
/// 虚拟化选择器（底部抽屉）。使用虚拟化的 CollectionView 显示可选项，
/// 支持子列表导航（PushItems/PopItems）、结果态替换（ReplaceItems），
/// 以及 Android 上对背后兄弟视图的高斯模糊。用于替代外观不佳的居中弹窗。
/// </summary>
public partial class VirtualizedSelect : ContentView
{
    public event EventHandler? Closed;

    private bool _isOpen;
    private readonly Stack<(IList<VirtualizedSelectItem> Items, string Title)> _navStack = new();
    private ObservableCollection<VirtualizedSelectItem> _items = new();

    private readonly Color _primary;
    private readonly Color _textPrimary;
    private readonly Color _textSecondary;
    private readonly Color _textHint;
    private readonly Color _chipInactive;

    public VirtualizedSelect()
    {
        InitializeComponent();

        _primary = (Color)Application.Current!.Resources["PrimaryColor"];
        _textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        _textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        _textHint = (Color)Application.Current!.Resources["TextHintColor"];
        _chipInactive = (Color)Application.Current!.Resources["ChipInactiveColor"];

        BuildItemTemplate();
    }

    // ─── 公开 API ───

    /// <summary>显示选择器（重置导航栈）。</summary>
    public void Show(IEnumerable<VirtualizedSelectItem> items, string title = "")
    {
        _navStack.Clear();
        SetCurrent(items, title);
        Open();
    }

    /// <summary>进入子列表（保留当前列表，可返回）。</summary>
    public void PushItems(IEnumerable<VirtualizedSelectItem> items, string title)
    {
        _navStack.Push((_items.ToList(), TitleLabel.Text ?? ""));
        SetCurrent(items, title);
    }

    /// <summary>就地替换当前列表（不进导航栈，用于结果/提示态）。</summary>
    public void ReplaceItems(IEnumerable<VirtualizedSelectItem> items, string title)
    {
        SetCurrent(items, title);
    }

    /// <summary>返回上一级列表。</summary>
    public void PopItems()
    {
        if (_navStack.Count > 0)
        {
            var (items, title) = _navStack.Pop();
            SetCurrent(items, title);
        }
    }

    private void SetCurrent(IEnumerable<VirtualizedSelectItem> items, string title)
    {
        _items = new ObservableCollection<VirtualizedSelectItem>(items);
        ItemsView.ItemsSource = _items;
        TitleLabel.Text = title;
        BackButton.IsVisible = _navStack.Count > 0;
    }

    // ─── 打开 / 关闭 / 动画 ───

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        this.InputTransparent = false;
        this.IsVisible = true;
        this.Opacity = 1;

        var screenH = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        SheetCard.MaximumHeightRequest = screenH * 0.82;

        MaskLayer.Opacity = 0;
        SheetCard.Opacity = 0;
        SheetCard.TranslationY = 600;

#if ANDROID
        ApplyBlurToSiblings();
#endif

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.WhenAll(
                MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                SheetCard.FadeTo(1, 200, Easing.CubicOut),
                SheetCard.TranslateTo(0, 0, 300, Easing.CubicOut)
            );
        });
    }

    public async void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        await Task.WhenAll(
            MaskLayer.FadeTo(0, 180, Easing.CubicIn),
            SheetCard.TranslateTo(0, 600, 220, Easing.CubicIn),
            SheetCard.FadeTo(0, 180, Easing.CubicIn)
        );

#if ANDROID
        RemoveBlurFromSiblings();
#endif

        this.Opacity = 0;
        this.IsVisible = false;
        this.InputTransparent = true;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaskTapped(object? sender, EventArgs e) => Close();
    private void OnCloseTapped(object? sender, EventArgs e) => Close();
    private void OnBackTapped(object? sender, EventArgs e) => PopItems();

    // ─── 条目模板 ───

    private void BuildItemTemplate()
    {
        ItemsView.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new() { Width = 44 },
                    new() { Width = GridLength.Star },
                    new() { Width = GridLength.Auto }
                },
                Padding = new Thickness(10, 4),
                ColumnSpacing = 12
            };

            // 左侧字形圆角玻璃圈
            var glyphCircle = new Border
            {
                WidthRequest = 40,
                HeightRequest = 40,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                StrokeThickness = 0,
                BackgroundColor = _chipInactive,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var glyph = new Label
            {
                FontSize = 18,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = _primary
            };
            glyph.SetBinding(Label.TextProperty, nameof(VirtualizedSelectItem.Icon));
            glyphCircle.Content = glyph;
            grid.Add(glyphCircle, 0);

            // 主/副文案
            var textStack = new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center
            };
            var titleLabel = new Label
            {
                FontSize = 15,
                TextColor = _textPrimary
            };
            titleLabel.SetBinding(Label.TextProperty, nameof(VirtualizedSelectItem.Text));
            textStack.Add(titleLabel);

            var subLabel = new Label
            {
                FontSize = 12,
                TextColor = _textSecondary
            };
            subLabel.SetBinding(Label.TextProperty, nameof(VirtualizedSelectItem.Subtitle));
            subLabel.SetBinding(Label.IsVisibleProperty, nameof(VirtualizedSelectItem.Subtitle),
                converter: new StringNonEmptyConverter());
            textStack.Add(subLabel);
            grid.Add(textStack, 1);

            // 右侧尾标（子列表箭头 / 选中对勾）
            var trailing = new Label
            {
                FontSize = 18,
                TextColor = _textHint,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            trailing.SetBinding(Label.TextProperty, nameof(VirtualizedSelectItem.TrailingIcon));
            grid.Add(trailing, 2);

            var row = new Border
            {
                BackgroundColor = Colors.Transparent,
                StrokeThickness = 0,
                Content = grid
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, _) =>
            {
                if (s is Border b && b.BindingContext is VirtualizedSelectItem item)
                    OnItemTapped(item);
            };
            row.GestureRecognizers.Add(tap);
            return row;
        });
    }

    private void OnItemTapped(VirtualizedSelectItem item)
    {
        item.OnSelected?.Invoke(item);
        if (!item.KeepOpen)
            Close();
    }

    // ─── Android 模糊背后兄弟视图 ───

#if ANDROID
    private readonly List<global::Android.Views.View> _blurredViews = new();

    private void ApplyBlurToSiblings()
    {
        _blurredViews.Clear();
        if (this.Parent is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child == this) continue;
                if (child is Microsoft.Maui.Controls.View view &&
                    view.Handler?.PlatformView is global::Android.Views.View nativeView)
                {
                    nativeView.SetRenderEffect(
                        global::Android.Graphics.RenderEffect.CreateBlurEffect(
                            24, 24, global::Android.Graphics.Shader.TileMode.Clamp));
                    _blurredViews.Add(nativeView);
                }
            }
        }
    }

    private void RemoveBlurFromSiblings()
    {
        foreach (var view in _blurredViews)
        {
            try { view.SetRenderEffect(null); } catch { }
        }
        _blurredViews.Clear();
    }
#endif

    private sealed class StringNonEmptyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => !string.IsNullOrEmpty(value as string);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
