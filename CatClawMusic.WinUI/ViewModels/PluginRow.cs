using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>插件管理页的行模型</summary>
public sealed partial class PluginRow : ObservableObject
{
    public PluginRow(PluginInfo info)
    {
        Info = info;
        IsEnabled = info.IsEnabled;
    }

    public PluginInfo Info { get; }

    public string Name => Info.DisplayName;

    public string Subtitle => $"{Info.Version} · {Info.Author}";

    public string Description => string.IsNullOrWhiteSpace(Info.Description) ? "（无描述）" : Info.Description;

    public bool CanUninstall => Info.CanUninstall;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }
}
