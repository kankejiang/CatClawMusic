using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Text;
using Windows.UI.Text;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 歌词行展示模型：当前行只改 <see cref="IsCurrent"/>，由派生属性驱动透明度/字重。
/// </summary>
public sealed partial class LyricLineRow : ObservableObject
{
    public LyricLineRow(LrcLyricLine line, int index)
    {
        Line = line;
        Index = index;
    }

    public LrcLyricLine Line { get; }

    public int Index { get; }

    public string Text => string.IsNullOrWhiteSpace(Line.Text) ? "♪" : Line.Text;

    public string? Translation => string.IsNullOrWhiteSpace(Line.Translation) ? null : Line.Translation;

    public string? Roma => string.IsNullOrWhiteSpace(Line.Roma) ? null : Line.Roma;

    public bool HasTranslation => Translation is not null;

    public bool HasRoma => Roma is not null;

    public TimeSpan Timestamp => Line.Timestamp;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public double LineOpacity => IsCurrent ? 1.0 : 0.45;

    public FontWeight Weight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(LineOpacity));
        OnPropertyChanged(nameof(Weight));
    }
}
