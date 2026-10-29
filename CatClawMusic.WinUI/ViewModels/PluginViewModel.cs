using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 插件管理页：安装 / 启用停用 / 卸载 `.ccp`。
/// 插件实例在启动时初始化；停用与安装都按 MAUI 的口径提示「重启后完全生效」。
/// </summary>
public sealed partial class PluginViewModel : ObservableObject
{
    private readonly PluginService _pluginService;
    private readonly FilePickerService _filePicker;

    public PluginViewModel(PluginService pluginService, FilePickerService filePicker)
    {
        _pluginService = pluginService;
        _filePicker = filePicker;
        StatusText = "还没有安装插件";
    }

    public ObservableCollection<PluginRow> Plugins { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsEmpty => Plugins.Count == 0;

    [RelayCommand]
    public void Load()
    {
        var all = _pluginService.Manager.GetAllPlugins();
        Plugins.Clear();
        foreach (var plugin in all)
            Plugins.Add(new PluginRow(plugin));

        var enabled = all.Count(p => p.IsEnabled);
        StatusText = all.Count == 0
            ? "还没有安装插件。宿主不带音源，装一个 .ccp 音源插件才能搜歌。"
            : $"共 {all.Count} 个插件，已启用 {enabled} 个";

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        var path = await _filePicker.PickPluginFileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsBusy = true;
        try
        {
            var info = await _pluginService.Manager.InstallFromLocalFileAsync(path);
            StatusText = info is null
                ? "安装失败：不是有效的 .ccp 插件"
                : $"已安装「{info.DisplayName}」，重启应用后生效";
            Load();
        }
        catch (Exception ex)
        {
            StatusText = $"安装失败：{ex.Message}";
            Log.Error("plugin", $"安装插件失败：{ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetEnabled(PluginRow row, bool enabled)
    {
        try
        {
            _pluginService.Manager.SetPluginEnabled(row.Info.PluginTypeId, enabled);
            row.IsEnabled = enabled;
            StatusText = enabled
                ? $"已启用「{row.Name}」，重启应用后生效"
                : $"已停用「{row.Name}」，重启应用后生效";
        }
        catch (Exception ex)
        {
            StatusText = $"切换失败：{ex.Message}";
        }
    }

    public async Task UninstallAsync(PluginRow row)
    {
        try
        {
            var ok = await _pluginService.Manager.UninstallPluginAsync(row.Info.PluginTypeId);
            StatusText = ok ? $"已卸载「{row.Name}」，重启应用后生效" : "卸载失败";
            Load();
        }
        catch (Exception ex)
        {
            StatusText = $"卸载失败：{ex.Message}";
            Log.Error("plugin", $"卸载插件失败：{ex}");
        }
    }

    [RelayCommand]
    private void OpenPluginFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.PluginDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("plugin", $"打开插件目录失败：{ex.Message}");
        }
    }
}
