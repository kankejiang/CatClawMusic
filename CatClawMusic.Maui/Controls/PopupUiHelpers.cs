using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 弹窗/面板通用 UI 构建辅助：圆角 Chip、开关行、横向滑块行、底部按钮。
/// 被 NowPlayingPage（定时关闭）与 EqualizerPage（音效中心）复用，避免重复实现。
/// </summary>
internal static class PopupUiHelpers
{
    /// <summary>创建 Chip 按钮（圆角标签）</summary>
    public static Border CreateChip(string text, bool active, bool compact = false)
    {
        var chip = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(compact ? 13 : 0, compact ? 8 : 11),
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        SetChipActive(chip, active);
        return chip;
    }

    public static void SetChipActive(Border chip, bool active)
    {
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        if (active)
        {
            chip.Background = new LinearGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(primary.WithAlpha(0.35f), 0f),
                    new(Color.FromArgb("#55D6FF").WithAlpha(0.25f), 1f)
                }
            };
            chip.Stroke = new SolidColorBrush(primary.WithAlpha(0.6f));
        }
        else
        {
            // 关键修复：必须改写 Background(Brush)，不能只改 BackgroundColor(Color)。
            // Border 上 Background(Brush) 与 BackgroundColor(Color) 是两个独立属性，渲染优先用 Background；
            // 之前选中态设了 Background=渐变刷，这里只改 BackgroundColor 不会覆盖它，旧选中态会残留
            // （表现为“点击一个按钮，其他按钮不被取消选中”）。统一用 Background 后两者切换干净。
            chip.Background = new SolidColorBrush(new Color(1, 1, 1, 0.06f));
            chip.Stroke = new SolidColorBrush(new Color(1, 1, 1, 0.08f));
        }
        chip.StrokeThickness = 1;
        // 清掉可能的 BackgroundColor 残留，彻底避免与 Background 刷冲突
        chip.BackgroundColor = null;
    }

    public static void UpdateChipStates(List<Border> chips, int activeIndex)
    {
        for (int i = 0; i < chips.Count; i++)
            SetChipActive(chips[i], i == activeIndex);
    }

    /// <summary>创建开关选项行</summary>
    public static View CreateToggleRow(string title, string description, bool initial, Action<bool> onChanged, Thickness? margin = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } },
            ColumnSpacing = 10
        };

        var textStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        textStack.Add(new Label
        {
            Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"]
        });
        textStack.Add(new Label
        {
            Text = description, FontSize = 11,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"]
        });
        grid.Add(textStack, 0);

        var toggle = new Switch
        {
            IsToggled = initial,
            VerticalOptions = LayoutOptions.Center,
            OnColor = (Color)Application.Current!.Resources["PrimaryColor"]
        };
        toggle.Toggled += (_, e) => onChanged(e.Value);
        grid.Add(toggle, 1);

        return new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            StrokeThickness = 0,
            Padding = new Thickness(12, 11),
            Margin = margin ?? new Thickness(0, 0, 0, 8),
            Content = grid
        };
    }

    /// <summary>创建弹窗底部按钮（渐变主按钮 / 玻璃幽灵按钮）</summary>
    public static Border CreatePopupButton(string text, bool primary)
    {
        var btn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = text,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = primary ? Color.FromArgb("#080B1A") : (Color)Application.Current!.Resources["TextPrimaryColor"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        if (primary)
        {
            var c1 = (Color)Application.Current!.Resources["PrimaryColor"];
            btn.Background = new LinearGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(c1, 0f),
                    new(Color.FromArgb("#55D6FF"), 1f)
                }
            };
            btn.StrokeThickness = 0;
        }
        else
        {
            btn.BackgroundColor = new Color(1, 1, 1, 0.06f);
            btn.Stroke = new SolidColorBrush(new Color(1, 1, 1, 0.14f));
            btn.StrokeThickness = 1;
        }
        return btn;
    }

    /// <summary>创建横向滑块行（音效增强用）</summary>
    public static View CreateHSliderRow(string label, double min, double max, double initial,
        Action<double> onChanged, Func<double, string> format)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 70 }, new() { Width = GridLength.Star }, new() { Width = 42 }
            },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 7)
        };

        grid.Add(new Label
        {
            Text = label, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            VerticalTextAlignment = TextAlignment.Center
        }, 0);

        var valueLabel = new Label
        {
            Text = format(initial), FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["PrimaryColor"],
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = initial,
            MinimumTrackColor = (Color)Application.Current!.Resources["PrimaryColor"],
            MaximumTrackColor = new Color(1, 1, 1, 0.12f),
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        slider.ValueChanged += (_, e) =>
        {
            valueLabel.Text = format(e.NewValue);
            onChanged(e.NewValue);
        };
        grid.Add(slider, 1);
        grid.Add(valueLabel, 2);
        return grid;
    }
}
