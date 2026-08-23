using System.Collections;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 一个网格行：内装不超过 <see cref="ChunkGridHelper.Chunk"/> 指定列数的条目。
/// WinUI 上 CollectionView 的 GridItemsLayout.Span 失效（项按整窗宽测量、单张占满），
/// 而 FlexLayout 一次性渲染全量条目会因过重闪崩（0xc000027b）。
/// 折中方案：外层 CollectionView 虚拟化「行」，每行水平排 N 张固定宽卡片。
/// </summary>
public class GridChunkRow
{
    public IList Items { get; } = new List<object>();
}

public static class ChunkGridHelper
{
    /// <summary>按列数把扁平列表切成若干行（不足一行补空位）。</summary>
    public static void Chunk(IReadOnlyList<object> source, int span,
        ObservableCollection<GridChunkRow> target)
    {
        target.Clear();
        if (span < 1) span = 1;
        GridChunkRow? row = null;
        int i = 0;
        foreach (var item in source)
        {
            if (i % span == 0) { row = new GridChunkRow(); target.Add(row); }
            row!.Items.Add(item);
            i++;
        }
    }

    /// <summary>由可用宽度 + 卡片目标宽度推导列数（窗口拉宽 → 列数变多）。</summary>
    public static int ComputeSpan(double availableWidth, double cardWidth, double spacing, int maxSpan)
    {
        var span = (int)Math.Floor((availableWidth + spacing) / (cardWidth + spacing));
        if (span < 1) span = 1;
        if (span > maxSpan) span = maxSpan;
        return span;
    }
}