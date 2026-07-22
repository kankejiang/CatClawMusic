using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CatClawMusic.Maui.Controls.EqBands;

/// <summary>单个均衡器频段：中心频率 + 当前增益(dB) + 显示标签。增益为整数 dB 步进。</summary>
public sealed class EqBand : INotifyPropertyChanged
{
    public double Frequency { get; }
    public string Label { get; }

    private double _value;
    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Round(value); // 整数 dB 步进，与拖拽一致
            if (_value == v) return;
            _value = v;
            OnPropertyChanged();
        }
    }

    public EqBand(double frequency, string label, double value)
    {
        Frequency = frequency;
        Label = label;
        _value = Math.Round(value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
