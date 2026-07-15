using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Maui.Services;
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
    private readonly ChatMemoryService _chatMemoryService;

    /// <summary>可选的 LLM 提供商列表</summary>
    public ObservableCollection<LlmProviderInfo> Providers { get; } = new();

    /// <summary>可选的预设模型列表（根据所选提供商动态变化）</summary>
    public ObservableCollection<string> PresetModels { get; } = new();

    /// <summary>当前默认 Agent 名称（只读展示，目前内置仅 Yuki）</summary>
    [ObservableProperty]
    private string _currentAgentName = "Yuki";

    /// <summary>配置名称，用于本地标识当前 LLM 配置</summary>
    [ObservableProperty]
    private string _configName = "默认配置";

    /// <summary>当前选中的 LLM 提供商信息（含 ID、默认 URL 与预设模型列表）</summary>
    [ObservableProperty]
    private LlmProviderInfo? _selectedProvider;

    /// <summary>LLM 服务的 API Base URL</summary>
    [ObservableProperty]
    private string _apiUrl = "https://api.deepseek.com/v1";

    /// <summary>LLM 服务的 API Key</summary>
    [ObservableProperty]
    private string _apiKey = "";

    /// <summary>模型名称，如 deepseek-chat</summary>
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

    /// <summary>是否正在获取模型列表</summary>
    [ObservableProperty]
    private bool _isFetchingModels;

    /// <summary>从 API 获取到的可用模型列表</summary>
    public ObservableCollection<string> FetchedModels { get; } = new();

    /// <summary>获取模型列表的结果提示</summary>
    [ObservableProperty]
    private string _fetchModelsResult = "";

    /// <summary>模型选择弹窗是否可见</summary>
    [ObservableProperty]
    private bool _isModelPickerVisible;

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

    /// <summary>当前配置名称</summary>
    [ObservableProperty]
    private string _currentConfigName = "";

    /// <summary>当前提供商显示名称</summary>
    [ObservableProperty]
    private string _currentProviderName = "";

    /// <summary>当前模型名称</summary>
    [ObservableProperty]
    private string _currentModel = "";

    /// <summary>当前 API URL</summary>
    [ObservableProperty]
    private string _currentApiUrl = "";

    /// <summary>是否存在已配置的当前模型</summary>
    [ObservableProperty]
    private bool _hasCurrentConfig;

    /// <summary>长期记忆内容</summary>
    [ObservableProperty]
    private string _memoryContent = "";

    /// <summary>回退模型列表</summary>
    [ObservableProperty]
    private ObservableCollection<LlmConfig> _fallbackConfigs = new();

    /// <summary>是否存在回退模型</summary>
    [ObservableProperty]
    private bool _hasFallbackConfigs;

    /// <summary>
    /// 初始化 <see cref="AiSettingsViewModel"/> 实例，并填充可选提供商列表。
    /// </summary>
    /// <param name="agentService">Agent 服务，用于读取/切换默认 Agent</param>
    /// <param name="llmClient">LLM 客户端，用于连接测试</param>
    /// <param name="configStorage">Agent 配置存储</param>
    /// <param name="chatMemoryService">聊天长期记忆服务</param>
    public AiSettingsViewModel(
        IAgentService agentService,
        ILlmClient llmClient,
        IAgentConfigStorage configStorage,
        ChatMemoryService chatMemoryService)
    {
        _agentService = agentService;
        _llmClient = llmClient;
        _configStorage = configStorage;
        _chatMemoryService = chatMemoryService;

        foreach (var p in LlmProviderInfo.GetAll())
            Providers.Add(p);
    }

    /// <summary>页面出现时加载已保存的配置</summary>
    public void OnAppearing()
    {
        LoadConfig();
        LoadFallbackConfigs();
        LoadMemoryContent();
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

            // 填充 Dashboard 展示属性
            CurrentConfigName = string.IsNullOrWhiteSpace(config.Name) ? "默认配置" : config.Name;
            CurrentProviderName = GetProviderDisplayName(config.Provider);
            CurrentModel = config.Model;
            CurrentApiUrl = config.ApiUrl;
            HasCurrentConfig = !string.IsNullOrWhiteSpace(config.ApiUrl)
                               && !string.IsNullOrWhiteSpace(config.ApiKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] LoadConfig 失败: {ex.Message}");
        }
    }

    /// <summary>根据提供商 ID 获取显示名称</summary>
    private static string GetProviderDisplayName(string providerId)
    {
        var provider = LlmProviderInfo.GetAll().FirstOrDefault(p => p.Id == providerId);
        return provider?.Name ?? (string.IsNullOrWhiteSpace(providerId) ? "自定义" : providerId);
    }

    /// <summary>加载回退模型列表（排除当前配置，仅保留 FallbackEnabled=true 的）</summary>
    private void LoadFallbackConfigs()
    {
        FallbackConfigs.Clear();
        try
        {
            var currentName = AgentService.GetCurrentConfigName();
            var allConfigs = AgentService.LoadAllConfigs();
            foreach (var c in allConfigs.Where(c => c.FallbackEnabled && c.Name != currentName))
                FallbackConfigs.Add(c);
            HasFallbackConfigs = FallbackConfigs.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] LoadFallbackConfigs 失败: {ex.Message}");
            HasFallbackConfigs = false;
        }
    }

    /// <summary>加载长期记忆内容</summary>
    private void LoadMemoryContent()
    {
        MemoryContent = _chatMemoryService.LoadMemory();
    }

    /// <summary>IsEnabled 变化时自动持久化到当前配置</summary>
    partial void OnIsEnabledChanged(bool value)
    {
        try
        {
            var config = AgentService.LoadConfig();
            if (config.Enabled == value) return;
            config.Enabled = value;
            AgentService.SaveConfig(config);
            RefreshConfiguredStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] 保存 Enabled 失败: {ex.Message}");
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

    /// <summary>从 API 获取可用模型列表</summary>
    [RelayCommand]
    public async Task FetchModelsAsync()
    {
        if (IsFetchingModels) return;

        if (string.IsNullOrWhiteSpace(ApiUrl) || string.IsNullOrWhiteSpace(ApiKey))
        {
            FetchModelsResult = "请先填写 Base URL 和 API Key";
            return;
        }

        IsFetchingModels = true;
        FetchModelsResult = "正在获取模型列表…";
        IsModelPickerVisible = false;
        FetchedModels.Clear();

        try
        {
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

            var models = await _llmClient.GetModelsAsync();
            if (models.Count == 0)
            {
                FetchModelsResult = "未获取到可用模型，请检查 API 权限";
                return;
            }

            foreach (var m in models)
                FetchedModels.Add(m);

            FetchModelsResult = $"共获取到 {models.Count} 个模型，请选择";
            IsModelPickerVisible = true;
        }
        catch (Exception ex)
        {
            FetchModelsResult = $"获取失败：{ex.Message}";
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    /// <summary>选择从 API 获取到的模型</summary>
    [RelayCommand]
    public void SelectFetchedModel(string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            Model = modelName;
            IsModelPickerVisible = false;
        }
    }

    /// <summary>关闭模型选择弹窗</summary>
    [RelayCommand]
    public void CloseModelPicker()
    {
        IsModelPickerVisible = false;
    }

    /// <summary>导航到模型管理页面</summary>
    [RelayCommand]
    private async Task ManageModelsAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("settings/modelmanager");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] 导航模型管理失败: {ex.Message}");
        }
    }

    /// <summary>添加一条长期记忆（弹出输入框）</summary>
    [RelayCommand]
    private async Task AddMemoryAsync()
    {
        try
        {
            string? input = null;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Application.Current?.MainPage is Page page)
                    input = await page.DisplayPromptAsync("添加记忆", "请输入要长期记住的内容：", "确定", "取消", maxLength: 500);
            });

            if (string.IsNullOrWhiteSpace(input)) return;

            await _chatMemoryService.AddMemoryAsync("preference", input, 7);
            LoadMemoryContent();
            await ToastAsync("记忆已添加");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] 添加记忆失败: {ex.Message}");
            await ToastAsync($"添加失败：{ex.Message}");
        }
    }

    /// <summary>清空所有长期记忆（带确认弹窗）</summary>
    [RelayCommand]
    private async Task ClearMemoryAsync()
    {
        try
        {
            var confirm = false;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Application.Current?.MainPage is Page page)
                    confirm = await page.DisplayAlert("确认", "确定要清空所有长期记忆吗？此操作不可撤销。", "清空", "取消");
            });

            if (!confirm) return;

            await _chatMemoryService.ClearMemoryAsync();
            LoadMemoryContent();
            await ToastAsync("记忆已清空");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiSettings] 清空记忆失败: {ex.Message}");
            await ToastAsync($"清空失败：{ex.Message}");
        }
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
