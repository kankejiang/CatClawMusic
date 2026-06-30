using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services.AI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// AI 设置页 ViewModel：负责 LLM 配置（Base URL / API Key / 模型 / 超时 / 启用）、
/// 提供商选择、连接测试、默认 Agent 切换。
/// </summary>
public partial class AiSettingsViewModel : ObservableObject
{
    private readonly IAgentService _agentService;
    private readonly ILlmClient _llmClient;
    private readonly IAgentConfigStorage _configStorage;

    /// <summary>可选的 LLM 提供商列表</summary>
    public ObservableCollection<LlmProviderInfo> Providers { get; } = new();

    /// <summary>可选的预设模型列表（根据所选提供商动态变化）</summary>
    public ObservableCollection<string> PresetModels { get; } = new();

    /// <summary>当前默认 Agent 名称（只读展示，目前内置仅 Yuki）</summary>
    [ObservableProperty]
    private string _currentAgentName = "Yuki";

    /// <summary>配置名称</summary>
    [ObservableProperty]
    private string _configName = "默认配置";

    /// <summary>提供商 ID</summary>
    [ObservableProperty]
    private LlmProviderInfo? _selectedProvider;

    /// <summary>API Base URL</summary>
    [ObservableProperty]
    private string _apiUrl = "https://api.deepseek.com/v1";

    /// <summary>API Key</summary>
    [ObservableProperty]
    private string _apiKey = "";

    /// <summary>模型名</summary>
    [ObservableProperty]
    private string _model = "deepseek-chat";

    /// <summary>温度（0-2）</summary>
    [ObservableProperty]
    private double _temperature = 0.7;

    /// <summary>最大 Tokens</summary>
    [ObservableProperty]
    private int _maxTokens = 2048;

    /// <summary>是否启用 AI</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>是否正在测试连接</summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>是否正在保存</summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>测试结果文本</summary>
    [ObservableProperty]
    private string _testResult = "";

    /// <summary>测试结果是否为成功</summary>
    [ObservableProperty]
    private bool _isTestSuccess;

    /// <summary>测试结果文本颜色</summary>
    [ObservableProperty]
    private string _testResultColor = "#F44336";

    /// <summary>当前是否已配置（只读展示）</summary>
    [ObservableProperty]
    private string _configuredStatus = "未配置";

    public AiSettingsViewModel(
        IAgentService agentService,
        ILlmClient llmClient,
        IAgentConfigStorage configStorage)
    {
        _agentService = agentService;
        _llmClient = llmClient;
        _configStorage = configStorage;

        foreach (var p in LlmProviderInfo.GetAll())
            Providers.Add(p);
    }

    /// <summary>页面出现时加载已保存的配置</summary>
    public void OnAppearing()
    {
        LoadConfig();
        RefreshConfiguredStatus();
    }

    private void LoadConfig()
    {
        try
        {
            var config = AgentService.LoadConfig();
            ConfigName = string.IsNullOrWhiteSpace(config.Name) ? "默认配置" : config.Name;
            ApiUrl = config.ApiUrl;
            ApiKey = config.ApiKey;
            Model = config.Model;
            Temperature = config.Temperature;
            MaxTokens = config.MaxTokens;
            IsEnabled = config.Enabled;

            SelectedProvider = Providers.FirstOrDefault(p => p.Id == config.Provider)
                               ?? Providers.FirstOrDefault(p => p.Id == "deepseek")
                               ?? Providers[0];

            RefreshPresetModels();
            CurrentAgentName = _agentService.GetCurrentAgent()?.Name ?? "Yuki";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] LoadConfig 失败: {ex.Message}");
        }
    }

    private void RefreshConfiguredStatus()
    {
        ConfiguredStatus = _agentService.IsConfigured
            ? $"已连接 · {_agentService.GetCurrentAgent().Name}"
            : "AI 助手未配置";
    }

    partial void OnSelectedProviderChanged(LlmProviderInfo? value)
    {
        if (value == null) return;
        // 切换提供商时，若 Base URL 为空或属于其他提供商预设，则更新为该提供商默认 URL
        if (string.IsNullOrWhiteSpace(ApiUrl) ||
            Providers.Any(p => p.DefaultApiUrl == ApiUrl && p != value))
        {
            ApiUrl = value.DefaultApiUrl;
        }
        RefreshPresetModels();
    }

    private void RefreshPresetModels()
    {
        PresetModels.Clear();
        if (SelectedProvider?.PresetModels == null) return;
        foreach (var m in SelectedProvider.PresetModels)
            PresetModels.Add(m);
    }

    /// <summary>保存当前配置</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        try
        {
            var config = new LlmConfig
            {
                Name = ConfigName,
                Provider = SelectedProvider?.Id ?? "custom",
                ApiUrl = ApiUrl?.Trim() ?? "",
                ApiKey = ApiKey?.Trim() ?? "",
                Model = Model?.Trim() ?? "",
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                Enabled = IsEnabled
            };
            AgentService.SaveConfig(config);
            RefreshConfiguredStatus();
            await ToastAsync("配置已保存");
        }
        catch (Exception ex)
        {
            await ToastAsync($"保存失败：{ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>测试当前配置的连通性</summary>
    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (IsTesting) return;

        // 先保存一份临时配置再测试，避免测试的是旧值
        if (string.IsNullOrWhiteSpace(ApiUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            TestResult = "请填写 Base URL 和 API Key";
            IsTestSuccess = false;
            return;
        }

        IsTesting = true;
        TestResult = "正在测试连接…";
        IsTestSuccess = false;

        try
        {
            // 临时保存配置，使 ILlmClient（基于 LoadConfig）读到最新值
            var tempConfig = new LlmConfig
            {
                Name = ConfigName,
                Provider = SelectedProvider?.Id ?? "custom",
                ApiUrl = ApiUrl.Trim(),
                ApiKey = ApiKey.Trim(),
                Model = Model?.Trim() ?? "",
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                Enabled = true
            };
            AgentService.SaveConfig(tempConfig);

            var ok = await _llmClient.TestConnectionAsync();
            TestResult = ok ? "连接成功 ✓" : "连接失败，请检查配置";
            IsTestSuccess = ok;
            TestResultColor = ok ? "#4CAF50" : "#F44336";
            RefreshConfiguredStatus();
        }
        catch (Exception ex)
        {
            TestResult = $"连接异常：{ex.Message}";
            IsTestSuccess = false;
            TestResultColor = "#F44336";
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>切换当前默认 Agent（目前仅 Yuki，保留扩展）</summary>
    [RelayCommand]
    public void SelectAgent(string agentId)
    {
        try
        {
            _agentService.SetCurrentAgent(agentId);
            CurrentAgentName = _agentService.GetCurrentAgent()?.Name ?? "Yuki";
            RefreshConfiguredStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] SelectAgent 失败: {ex.Message}");
        }
    }

    /// <summary>重置为默认值</summary>
    [RelayCommand]
    public void ResetToDefault()
    {
        SelectedProvider = Providers.FirstOrDefault(p => p.Id == "deepseek") ?? Providers[0];
        ApiUrl = "https://api.deepseek.com/v1";
        ApiKey = "";
        Model = "deepseek-chat";
        Temperature = 0.7;
        MaxTokens = 2048;
        IsEnabled = false;
        TestResult = "";
        IsTestSuccess = false;
    }

    /// <summary>使用预设模型填充模型输入框</summary>
    [RelayCommand]
    public void UsePresetModel(string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
            Model = modelName;
    }

    private static async Task ToastAsync(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage is Page page)
                await page.DisplayAlert("提示", message, "确定");
        });
    }
}
