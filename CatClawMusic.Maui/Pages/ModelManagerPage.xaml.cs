using System.Collections.ObjectModel;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Helpers;

namespace CatClawMusic.Maui.Pages;

/// <summary>模型管理页面，用于管理 AI 模型列表的增删改查。</summary>
public partial class ModelManagerPage : ContentPage
{
    /// <summary>当前持有的模型配置列表（含主模型/回退标识）。</summary>
    public ObservableCollection<LlmConfigEntry> Configs { get; } = new();

    /// <summary>列表中是否存在配置项，用于控制空提示的显示。</summary>
    public bool HasConfigs => Configs.Count > 0;

    /// <summary>初始化 <see cref="ModelManagerPage"/> 类的新实例。</summary>
    public ModelManagerPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    /// <summary>页面显示时重新加载配置列表，刷新主模型标识。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, false);
        LoadConfigs();
        LoadAgentRunSettings();
    }

    /// <summary>加载 Agent 全局运行设置到 UI 控件。</summary>
    private void LoadAgentRunSettings()
    {
        MaxToolRoundsEntry.Text = AgentService.GetMaxToolRounds().ToString();
        MaxPlanRoundsEntry.Text = AgentService.GetMaxPlanRounds().ToString();
    }

    /// <summary>执行轮数上限变更：即时保存。</summary>
    private void OnMaxToolRoundsChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (int.TryParse(MaxToolRoundsEntry.Text?.Trim() ?? "", out var val) && val >= 0)
            AgentService.SetMaxToolRounds(val);
    }

    /// <summary>规划轮数上限变更：即时保存。</summary>
    private void OnMaxPlanRoundsChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (int.TryParse(MaxPlanRoundsEntry.Text?.Trim() ?? "", out var val) && val >= 0)
            AgentService.SetMaxPlanRounds(val);
    }

    private void LoadConfigs()
    {
        Configs.Clear();
        try
        {
            var currentName = AgentService.GetCurrentConfigName();
            var all = AgentService.LoadAllConfigs();

            // 清理旧代码遗留的空默认配置（名为"默认配置"且未填写 ApiKey）
            var staleDefault = all.FirstOrDefault(c => c.Name == "默认配置" && string.IsNullOrEmpty(c.ApiKey));
            if (staleDefault != null)
            {
                all.Remove(staleDefault);
                AgentService.SaveAllConfigs(all);
            }

            // 清理旧版本测试连接时遗留的临时配置
            var staleTemps = all.Where(c => c.Name == "__temp__").ToList();
            if (staleTemps.Count > 0)
            {
                foreach (var t in staleTemps) all.Remove(t);
                AgentService.SaveAllConfigs(all);
                // 若当前主模型被清理掉，回退到第一个可用配置
                if (currentName == "__temp__" && all.Count > 0)
                {
                    AgentService.SetCurrentConfigName(all[0].Name);
                    currentName = all[0].Name;
                }
            }

            foreach (var c in all)
            {
                Configs.Add(new LlmConfigEntry
                {
                    Name = c.Name,
                    Provider = c.Provider,
                    ApiUrl = c.ApiUrl,
                    ApiKey = c.ApiKey,
                    Model = c.Model,
                    Temperature = c.Temperature,
                    MaxTokens = c.MaxTokens,
                    Enabled = c.Enabled,
                    FallbackEnabled = c.FallbackEnabled,
                    IsActive = c.Name == currentName
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug("ModelManagerPage.xaml", $"[ModelManager] LoadConfigs 失败: {ex.Message}");
        }
        OnPropertyChanged(nameof(HasConfigs));
    }

    /// <summary>点击右上角 + 按钮，导航到模型编辑页（新增模式，不带 id）。</summary>
    private void OnAddClicked(object? sender, EventArgs e)
    {
        try
        {
            var page = MauiProgram.Services.GetRequiredService<ModelEditPage>();
            OpenEditPage(page);
        }
        catch (Exception ex)
        {
            Log.Debug("ModelManagerPage", $"[ModelManager] 打开新建页失败: {ex.Message}");
        }
    }

    /// <summary>打开模型编辑页：Shell 环境（Android/竖屏）直接 PushAsync 压栈（绕开相对路由
    /// "settings/modeledit" 与导航栈中 settings 页的解析冲突导致的静默失败）；
    /// 桌面无 Shell 时嵌入主区域并保留返回按钮。</summary>
    private static void OpenEditPage(ModelEditPage page)
    {
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null)
        {
            _ = shell.Navigation.PushAsync(page);
            return;
        }
        DesktopNavigation.OpenEmbedded(page, hideBack: false);
    }

    /// <summary>点击「设为主模型」按钮，将指定配置设为当前主模型。</summary>
    private async void OnSetMainClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string name)
        {
            try
            {
                AgentService.SetCurrentConfigName(name);
                var config = AgentService.LoadAllConfigs().FirstOrDefault(c => c.Name == name);
                if (config != null)
                {
                    config.Enabled = true;
                    AgentService.SaveConfig(config);
                }
                LoadConfigs();
            }
            catch (Exception ex)
            {
                await AlertAsync("提示", $"设为主模型失败：{ex.Message}");
            }
        }
    }

    /// <summary>点击「切换备」按钮，切换指定配置的回退启用状态。</summary>
    private async void OnToggleFallbackClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string name)
        {
            try
            {
                var config = AgentService.LoadAllConfigs().FirstOrDefault(c => c.Name == name);
                if (config != null)
                {
                    config.FallbackEnabled = !config.FallbackEnabled;
                    // 设为备用即顺带启用：便于用户理解；退回候选不再要求 Enabled
                    //（GetFallbackConfigs 已去除该过滤），此自动启用仅作体验优化
                    if (config.FallbackEnabled)
                        config.Enabled = true;
                    AgentService.SaveConfig(config);
                    LoadConfigs();
                }
            }
            catch (Exception ex)
            {
                await AlertAsync("提示", $"切换回退失败：{ex.Message}");
            }
        }
    }

    /// <summary>点击「编辑」按钮，导航到模型编辑页（编辑模式，带 id）。</summary>
    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string name)
        {
            try
            {
                var page = MauiProgram.Services.GetRequiredService<ModelEditPage>();
                page.ConfigId = name;
                OpenEditPage(page);
            }
            catch (Exception ex)
            {
                Log.Debug("ModelManagerPage", $"[ModelManager] 打开编辑页失败: {ex.Message}");
            }
        }
    }

    /// <summary>点击「删除」按钮，弹出确认后删除指定配置。</summary>
    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string name)
        {
            var confirm = await AlertAsync("确认删除", $"确定要删除配置「{name}」吗？此操作不可撤销。", "删除", "取消");
            if (!confirm) return;

            try
            {
                AgentService.DeleteConfig(name);
                LoadConfigs();
            }
            catch (Exception ex)
            {
                await AlertAsync("提示", $"删除失败：{ex.Message}");
            }
        }
    }

    /// <summary>弹提示框：Windows 桌面嵌入模式本页不在窗口视觉树（Page.Window 为 null），
    /// DisplayAlert 会静默失败——改由窗口根页面调用（与下载管理同一修复模式）。</summary>
    private Task AlertAsync(string title, string message, string cancel = "确定")
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root != null && root != this)
            return root.DisplayAlert(title, message, cancel);
        return DisplayAlert(title, message, cancel);
    }

    /// <summary>弹选择框（确认/取消双按钮）：同上，Windows 嵌入模式用窗口根页面</summary>
    private Task<bool> AlertAsync(string title, string message, string accept, string cancel)
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root != null && root != this)
            return root.DisplayAlert(title, message, accept, cancel);
        return DisplayAlert(title, message, accept, cancel);
    }
}
