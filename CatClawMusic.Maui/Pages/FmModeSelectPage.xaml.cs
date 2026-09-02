using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 私人漫游推荐模式全屏选择页（样式参考播放列表页 <see cref="NowPlayingQueuePage"/>）：
/// 顶部返回按钮 + 标题栏 + 滚动模式列表。内容由插件返回的模式数据动态构建——
/// 顶部 3 个推荐模式卡（横排）+ 下方场景模式 4 列网格，选中项高亮。
/// 推入后播放页整体进后台，动态渲染/歌词定时器停止，滚动不再与底层渲染抢主线程。
/// </summary>
public partial class FmModeSelectPage : ContentPage
{
    private readonly NowPlayingViewModel _vm;

    public FmModeSelectPage(NowPlayingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // 与播放列表页一致：从屏幕底部滑入转场（首次布局完成后执行一次）
        RootGrid.SizeChanged += OnRootGridSizeChanged;
    }

    private void OnRootGridSizeChanged(object? sender, EventArgs e)
    {
        RootGrid.SizeChanged -= OnRootGridSizeChanged;

        if (RootGrid.Width <= 0 || RootGrid.Height <= 0) return;
        var h = RootGrid.Height;
        RootGrid.TranslationY = h;
        _ = RootGrid.TranslateTo(0, 0, 280, Easing.CubicOut);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 转场兜底：若布局就绪事件已过（二次进入），强制回到原位
        if (RootGrid.TranslationY != 0)
            _ = RootGrid.TranslateTo(0, 0, 280, Easing.CubicOut);

        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 结束转场动画，避免返回时残留位移（下一次推入会重新走滑入）
        this.CancelAnimations();
        RootGrid.TranslationY = 0;
    }

    /// <summary>从当前歌曲所属插件加载模式列表并渲染（重复进入时重建，反映最新选中态）</summary>
    private async Task LoadAsync()
    {
        var (categories, currentLabel) = await _vm.LoadFmModeCategoriesAsync();

        // 一次性缓存资源查找
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current.Resources["TextSecondaryColor"];
        var cardBg = (Color)Application.Current.Resources["CardBackgroundColor"];
        var stroke = (Color)Application.Current.Resources["GlassStrokeColor"];
        var accentPink = Color.FromArgb("#f953c6");
        var accentBg = Color.FromArgb("#1Af953c6");

        SubtitleLabel.Text = string.IsNullOrEmpty(currentLabel) ? "私人漫游 · 推荐模式" : $"当前模式：{currentLabel}";

        var root = new VerticalStackLayout { Spacing = 16 };
        var modes = categories.Where(c => c.Type == "mode").ToList();
        var scenes = categories.Where(c => c.Type == "scene").ToList();

        // 推荐模式（3 个横排卡片）
        if (modes.Count > 0)
        {
            root.Children.Add(SectionLabel("推荐模式", textSecondary));
            var modeRow = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            for (int i = 0; i < modes.Count; i++)
            {
                var isCurrent = modes[i].Title == currentLabel;
                var card = CreateFmModeCard(modes[i], isCurrent, textPrimary, textSecondary, cardBg, stroke, accentPink, accentBg);
                Grid.SetColumn(card, i);
                modeRow.Add(card);
            }
            root.Children.Add(modeRow);
        }

        // 场景模式（4 列网格）
        if (scenes.Count > 0)
        {
            root.Children.Add(SectionLabel("场景模式", textSecondary));
            int cols = 4;
            int rows = (int)Math.Ceiling(scenes.Count / (double)cols);
            var grid = new Grid { RowSpacing = 10, ColumnSpacing = 10 };
            for (int r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            for (int i = 0; i < scenes.Count; i++)
            {
                var s = scenes[i];
                var isCurrent = s.Title == currentLabel;
                var chip = CreateFmSceneChip(s, isCurrent, textPrimary, cardBg, accentPink, accentBg);
                grid.Add(chip, i % cols, i / cols);
            }
            root.Children.Add(grid);
        }

        ContentRoot.Children.Clear();
        ContentRoot.Children.Add(root);
    }

    private static Label SectionLabel(string text, Color color) => new()
    {
        Text = text,
        FontSize = 12,
        TextColor = color,
        HorizontalOptions = LayoutOptions.Start,
    };

    /// <summary>推荐模式卡（带标题+副标题+图标，选中高亮）</summary>
    private Border CreateFmModeCard(FmModeCategory m, bool isCurrent,
        Color textPrimary, Color textSecondary, Color cardBg, Color stroke, Color accentPink, Color accentBg)
    {
        var border = new Border
        {
            Padding = new Thickness(14, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = isCurrent ? 2 : 1,
            Stroke = isCurrent ? accentPink : stroke,
            BackgroundColor = isCurrent ? accentBg : cardBg,
            HorizontalOptions = LayoutOptions.Fill,
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await _vm.SelectFmModeAsync(m.Code);
                GoBack();
            }),
        });
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(new Label { Text = $"{m.Icon} {m.Title}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = textPrimary });
        if (!string.IsNullOrEmpty(m.SubTitle))
            stack.Children.Add(new Label { Text = m.SubTitle, FontSize = 10, TextColor = textSecondary, MaxLines = 2 });
        border.Content = stack;
        return border;
    }

    /// <summary>场景模式 Chip（圆角小卡片，选中高亮）</summary>
    private Border CreateFmSceneChip(FmModeCategory s, bool isCurrent,
        Color textPrimary, Color cardBg, Color accentPink, Color accentBg)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = isCurrent ? 2 : 0,
            Stroke = isCurrent ? accentPink : Colors.Transparent,
            BackgroundColor = isCurrent ? accentBg : cardBg,
            HorizontalOptions = LayoutOptions.Fill,
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await _vm.SelectFmModeAsync(s.Code);
                GoBack();
            }),
        });
        border.Content = new Label
        {
            Text = s.Title,
            FontSize = 12,
            FontAttributes = isCurrent ? FontAttributes.Bold : FontAttributes.None,
            TextColor = textPrimary,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        return border;
    }

    private void OnBackClicked(object? sender, EventArgs e) => GoBack();

    private void GoBack()
    {
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
        {
            _ = shell.Navigation.PopAsync();
            return;
        }
        if (shell != null)
        {
            _ = shell.GoToAsync("..");
            return;
        }
        DesktopNavigation.CloseEmbedded();
    }
}