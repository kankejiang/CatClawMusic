using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Devices;

namespace CatClawMusic.Maui.Controls.EqBands;

/// <summary>
/// 均衡器频段交互视图（开箱即用的原生实现）。
/// 每个频段使用一个原生 MAUI <see cref="Slider"/>，整体旋转 90° 变为竖向；
/// 拖拽走平台原生触摸管线（Android SeekBar / iOS UISlider），手感稳定、不会卡死。
/// 不再有任何自定义 Pointer 手势或页面滚动锁定逻辑。
/// 滑块下方显示 dB 数值标签与频率标签。
/// 列布局：频段数等于列数，全部用 GridLength.Star 均分面板宽度 → 5/10 段都均匀铺满、不裁切。
/// </summary>
public sealed class EqBandsView : Grid
{
    private readonly GainConverter _gainConverter = new();
    private IList<EqBand>? _bands;

    /// <summary>拖拽过程中每次增益变化 → 实时应用 EQ（原生模式即时生效，FFmpeg 模式内部防抖）。</summary>
    public event Action? LiveValueChanged;
    /// <summary>拖拽结束 → 标记自定义预设并持久化。</summary>
    public event Action? DragCompleted;

    public Color Accent { get; set; } = Colors.White;
    public Color LabelColor { get; set; } = new Color(1, 1, 1, 0.6f);
    public double MinGain { get; set; } = -12;
    public double MaxGain { get; set; } = 12;

    public const double TrackLength = 210;   // 滑块竖向长度（旋转后的竖向长度）

    public IList<EqBand>? Bands
    {
        get => _bands;
        set { _bands = value; Build(); }
    }

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        if (_bands == null) return;

        int n = _bands.Count;

        // 每列等宽（Star）均分面板宽度，保证 5/10 段都均匀铺满、不被裁切
        for (int c = 0; c < n; c++)
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(TrackLength) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 滑块旋转后的横向厚度：依据屏幕可用宽度估算，段多时收窄避免互相压住
        double screenWidth = DeviceDisplay.Current.MainDisplayInfo.Width
                             / DeviceDisplay.Current.MainDisplayInfo.Density;
        double reserve = 100;  // 页面左右内边距 + 面板内边距 + 左侧 dB 刻度列 + 间距
        double available = Math.Max(200, screenWidth - reserve);
        double colW = available / n;
        double trackThickness = Math.Min(40, Math.Max(18, colW - 6));

        for (int i = 0; i < n; i++)
        {
            var band = _bands[i];

            var slider = new Slider
            {
                Minimum = MinGain,
                Maximum = MaxGain,
                Value = band.Value,
                Rotation = 90,                 // 旋转为竖向：最大增益在顶部
                WidthRequest = TrackLength,    // 旋转后成为竖向长度
                HeightRequest = trackThickness, // 旋转后成为轨道横向厚度
                MinimumTrackColor = Accent,    // 底部到滑块的填充（增益越高柱越高）
                MaximumTrackColor = new Color(1, 1, 1, 0.12f),
                ThumbColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            // 模型为单一真源：双向绑定，拖拽写回 band.Value，标签/填充自动更新
            slider.SetBinding(Slider.ValueProperty,
                new Binding(nameof(EqBand.Value)) { Source = band, Mode = BindingMode.TwoWay });
            slider.ValueChanged += (_, _) => LiveValueChanged?.Invoke();
            slider.DragCompleted += (_, _) => DragCompleted?.Invoke();

            var valLabel = new Label
            {
                FontSize = 11, FontAttributes = FontAttributes.Bold,
                TextColor = Accent, HorizontalTextAlignment = TextAlignment.Center
            };
            valLabel.SetBinding(Label.TextProperty,
                new Binding(nameof(EqBand.Value)) { Source = band, Converter = _gainConverter });

            var hzLabel = new Label
            {
                Text = band.Label, FontSize = 10, TextColor = LabelColor,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            var labels = new VerticalStackLayout
            {
                Spacing = 1, HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            labels.Add(valLabel);
            labels.Add(hzLabel);

            var cell = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(TrackLength) },
                    new RowDefinition { Height = GridLength.Auto }
                }
            };
            cell.Add(slider, 0, 0);
            cell.Add(labels, 0, 1);
            Children.Add(cell);
            cell.SetValue(ColumnProperty, i);
            cell.SetValue(RowProperty, 0);
        }
    }
}
