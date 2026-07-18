using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 自绘环形图：根据绑定的 <see cref="PieDataset"/> 绘制多段圆环 + 中心总数。
/// 采用 GraphicsView（Microsoft.Maui.Graphics）而非第三方图表库，
/// 保证 Android / Windows 视觉一致且零额外依赖。
/// </summary>
public class DonutView : GraphicsView
{
    public static readonly BindableProperty DatasetProperty =
        BindableProperty.Create(
            nameof(Dataset),
            typeof(PieDataset),
            typeof(DonutView),
            defaultValue: null,
            propertyChanged: OnDatasetChanged);

    public PieDataset? Dataset
    {
        get => (PieDataset?)GetValue(DatasetProperty);
        set => SetValue(DatasetProperty, value);
    }

    public DonutView()
    {
        HeightRequest = 138;
        WidthRequest = 138;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        BackgroundColor = Colors.Transparent;
    }

    private static void OnDatasetChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (DonutView)bindable;
        view.Drawable = newValue is PieDataset ds ? new DonutDrawable(ds) : null;
        view.Invalidate();
    }
}

internal sealed class DonutDrawable : IDrawable
{
    private readonly PieDataset _dataset;

    public DonutDrawable(PieDataset dataset) => _dataset = dataset;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var total = _dataset.Total;
        if (total <= 0 || _dataset.Segments.Count == 0)
            return;

        float size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        float cx = dirtyRect.Center.X;
        float cy = dirtyRect.Center.Y;
        float radius = size / 2f - 10f;
        const float stroke = 15f;

        // 背景轨道
        canvas.StrokeColor = Color.FromArgb("#2A3870");
        canvas.StrokeSize = stroke;
        canvas.DrawCircle(cx, cy, radius);

        // 分段（顺时针，从正上方 -90° 起）
        // 注意：DrawArc 的 (x, y) 是椭圆外接框左上角，宽高=直径；而 DrawCircle 的 (cx,cy) 是圆心。
        // 必须用 cx-radius / cy-radius / 2*radius 才能与轨道圆完全重合，否则分段会偏小且偏右下。
        float angle = -90f;
        float boxX = cx - radius;
        float boxY = cy - radius;
        float boxSize = radius * 2f;
        foreach (var seg in _dataset.Segments)
        {
            float frac = total > 0 ? (float)seg.Count / total : 0f;
            if (frac <= 0f) continue;
            float sweep = frac * 360f;
            canvas.StrokeColor = seg.Color;
            canvas.StrokeSize = stroke;
            canvas.DrawArc(boxX, boxY, boxSize, boxSize, angle, angle + sweep, true, false);
            angle += sweep;
        }

        // 中心总数（大号粗体）
        canvas.StrokeSize = 0f;
        canvas.FontSize = 21f;
        canvas.Font = new Microsoft.Maui.Graphics.Font(
            Microsoft.Maui.Graphics.Font.Default.Name,
            (int)Microsoft.Maui.Graphics.FontWeights.Bold,
            Microsoft.Maui.Graphics.FontStyleType.Normal);
        canvas.FontColor = Color.FromArgb("#F7F8FF");
        canvas.DrawString(total.ToString("N0"), cx - radius, cy - 24f, radius * 2f, 28f,
            Microsoft.Maui.Graphics.HorizontalAlignment.Center,
            Microsoft.Maui.Graphics.VerticalAlignment.Center,
            Microsoft.Maui.Graphics.TextFlow.ClipBounds, 0f);

        // 中心单位（小号常规）
        canvas.FontSize = 10f;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontColor = Color.FromArgb("#8D93B7");
        canvas.DrawString("首 (本地)", cx - radius, cy - 2f, radius * 2f, 24f,
            Microsoft.Maui.Graphics.HorizontalAlignment.Center,
            Microsoft.Maui.Graphics.VerticalAlignment.Center,
            Microsoft.Maui.Graphics.TextFlow.ClipBounds, 0f);
    }
}
