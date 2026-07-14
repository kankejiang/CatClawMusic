using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services.AI;

namespace CatClawMusic.Maui.Pages;

/// <summary>模型编辑页面，用于编辑 AI 模型的配置信息。</summary>
[QueryProperty(nameof(ConfigId), "id")]
public partial class ModelEditPage : ContentPage
{
    private readonly ILlmClient _llmClient;
    private readonly IAgentService _agentService;

    /// <summary>可选的 LLM 提供商列表（供 Picker 绑定）。</summary>
    public ObservableCollection<LlmProviderInfo> Providers { get; } = new();

    /// <summary>当前所选提供商对应的预设模型列表。</summary>
    public ObservableCollection<string> PresetModels { get; } = new();

    /// <summary>从 API 获取到的可用模型列表。</summary>
    public ObservableCollection<string> FetchedModels { get; } = new();

    /// <summary>编辑模式下的原始配置名称（用于区分新建与编辑，以及重命名场景）。</summary>
    private string _originalName = "";

    /// <summary>是否正在加载（用于避免 SelectedIndexChanged 在初始化时触发）。</summary>
    private bool _isLoading;

    /// <summary>是否为新建模式（新建时自动生成配置名称）。</summary>
    private bool _isNewConfig;

    /// <summary>获取或设置导航查询参数中的配置 id（即配置名称）。</summary>
    public string ConfigId
    {
        get => _originalName;
        set
        {
            _originalName = Uri.UnescapeDataString(value ?? "");
        }
    }

    /// <summary>初始化 <see cref="ModelEditPage"/> 类的新实例，注入 LLM 客户端与智能体服务。</summary>
    /// <param name="llmClient">LLM 客户端，用于获取模型列表与测试连接。</param>
    /// <param name="agentService">智能体服务，用于检查配置状态。</param>
    public ModelEditPage(ILlmClient llmClient, IAgentService agentService)
    {
        InitializeComponent();
        _llmClient = llmClient;
        _agentService = agentService;
        BindingContext = this;

        foreach (var p in LlmProviderInfo.GetAll())
            Providers.Add(p);

        ProviderPicker.ItemsSource = Providers;

        // 推理力度选项
        ReasoningEffortPicker.ItemsSource = new List<string> { "auto", "disabled", "high", "max" };
        ReasoningEffortPicker.SelectedIndex = 1; // 默认 disabled

        // 响应格式选项
        ResponseFormatPicker.ItemsSource = new List<string> { "text", "json_object" };
        ResponseFormatPicker.SelectedIndex = 0; // 默认 text
    }

    /// <summary>页面显示时根据是否带 id 加载现有配置或准备新建。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, false);

        _isLoading = true;
        try
        {
            if (!string.IsNullOrEmpty(_originalName))
            {
                TitleLabel.Text = "编辑模型";
                _isNewConfig = false;
                LoadExistingConfig(_originalName);
            }
            else
            {
                TitleLabel.Text = "新建模型";
                _isNewConfig = true;
                ApplyDefaultValues();
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadExistingConfig(string name)
    {
        try
        {
            var all = AgentService.LoadAllConfigs();
            var config = all.FirstOrDefault(c => c.Name == name);
            if (config == null)
            {
                ApplyDefaultValues();
                return;
            }

            NameEntry.Text = config.Name;
            ApiUrlEntry.Text = config.ApiUrl;
            ApiKeyEntry.Text = config.ApiKey;
            ModelEntry.Text = config.Model;
            EnabledSwitch.IsToggled = config.Enabled;
            FallbackSwitch.IsToggled = config.FallbackEnabled;

            // 高级参数
            SetPickerSelection(ReasoningEffortPicker, config.ReasoningEffort, "disabled");
            SetPickerSelection(ResponseFormatPicker, config.ResponseFormat, "text");
            TopPSlider.Value = config.TopP;
            FrequencyPenaltySlider.Value = config.FrequencyPenalty;
            PresencePenaltySlider.Value = config.PresencePenalty;
            MaxCompletionTokensEntry.Text = config.MaxCompletionTokens.ToString();
            ContextCachingSwitch.IsToggled = config.ContextCaching;

            var provider = Providers.FirstOrDefault(p => p.Id == config.Provider)
                           ?? Providers.FirstOrDefault(p => p.Id == "deepseek")
                           ?? Providers[0];
            ProviderPicker.SelectedItem = provider;
            RefreshPresetModels(provider);
            UpdateSliderLabels();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModelEdit] LoadExistingConfig 失败: {ex.Message}");
            ApplyDefaultValues();
        }
    }

    private void ApplyDefaultValues()
    {
        NameEntry.Text = "";
        ApiUrlEntry.Text = "https://api.deepseek.com/v1";
        ApiKeyEntry.Text = "";
        ModelEntry.Text = "deepseek-chat";
        EnabledSwitch.IsToggled = true;
        FallbackSwitch.IsToggled = false;

        // 高级参数默认值
        SetPickerSelection(ReasoningEffortPicker, "disabled", "disabled");
        SetPickerSelection(ResponseFormatPicker, "text", "text");
        TopPSlider.Value = 1.0;
        FrequencyPenaltySlider.Value = 0;
        PresencePenaltySlider.Value = 0;
        MaxCompletionTokensEntry.Text = "0";
        ContextCachingSwitch.IsToggled = true;

        var provider = Providers.FirstOrDefault(p => p.Id == "deepseek") ?? Providers[0];
        ProviderPicker.SelectedItem = provider;
        RefreshPresetModels(provider);
        UpdateSliderLabels();
    }

    /// <summary>设置 Picker 的选中项，找不到时回退到默认值</summary>
    private static void SetPickerSelection(Picker picker, string? value, string defaultValue)
    {
        var list = picker.ItemsSource as IList<string>;
        var idx = list?.IndexOf(value ?? "") ?? -1;
        if (idx < 0) idx = list?.IndexOf(defaultValue) ?? -1;
        picker.SelectedIndex = idx;
    }

    private void RefreshPresetModels(LlmProviderInfo? provider)
    {
        PresetModels.Clear();
        if (provider?.PresetModels == null) return;
        foreach (var m in provider.PresetModels)
            PresetModels.Add(m);
    }

    /// <summary>提供商下拉选择变化时，自动填充默认 URL 和模型，并刷新预设模型列表。</summary>
    private void OnProviderSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        if (ProviderPicker.SelectedItem is not LlmProviderInfo provider) return;

        // 仅当当前 URL 为空或属于其他提供商预设时，才覆盖为该提供商的默认 URL
        var currentUrl = ApiUrlEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(currentUrl) ||
            Providers.Any(p => p.DefaultApiUrl == currentUrl && p.Id != provider.Id))
        {
            ApiUrlEntry.Text = provider.DefaultApiUrl;
        }

        // 模型为空或仍为其他提供商预设模型时，填入默认模型
        var currentModel = ModelEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(currentModel) ||
            Providers.Any(p => p.PresetModels?.Contains(currentModel) == true && p.Id != provider.Id))
        {
            ModelEntry.Text = provider.DefaultModel;
        }

        RefreshPresetModels(provider);
        AutoFillConfigName();
    }

    /// <summary>新建模式下自动填写配置名称：提供商名 + 模型名</summary>
    private void AutoFillConfigName()
    {
        if (!_isNewConfig) return;
        if (ProviderPicker.SelectedItem is not LlmProviderInfo provider) return;
        var model = ModelEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(model)) return;
        NameEntry.Text = $"{provider.Name}-{model}";
    }

    private void OnTopPValueChanged(object? sender, ValueChangedEventArgs e)
    {
        TopPValueLabel.Text = TopPSlider.Value.ToString("F2");
    }

    private void OnFrequencyPenaltyValueChanged(object? sender, ValueChangedEventArgs e)
    {
        FrequencyPenaltyValueLabel.Text = FrequencyPenaltySlider.Value.ToString("F1");
    }

    private void OnPresencePenaltyValueChanged(object? sender, ValueChangedEventArgs e)
    {
        PresencePenaltyValueLabel.Text = PresencePenaltySlider.Value.ToString("F1");
    }

    /// <summary>批量刷新所有滑块数值标签</summary>
    private void UpdateSliderLabels()
    {
        TopPValueLabel.Text = TopPSlider.Value.ToString("F2");
        FrequencyPenaltyValueLabel.Text = FrequencyPenaltySlider.Value.ToString("F1");
        PresencePenaltyValueLabel.Text = PresencePenaltySlider.Value.ToString("F1");
    }

    /// <summary>点击预设模型标签，填入模型输入框。</summary>
    private void OnPresetModelTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string modelName && !string.IsNullOrWhiteSpace(modelName))
        {
            ModelEntry.Text = modelName;
            AutoFillConfigName();
        }
    }

    /// <summary>点击从 API 获取的模型标签，填入模型输入框。</summary>
    private void OnFetchedModelTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string modelName && !string.IsNullOrWhiteSpace(modelName))
        {
            ModelEntry.Text = modelName;
            AutoFillConfigName();
        }
    }

    /// <summary>点击「获取模型列表」按钮，临时保存配置后调用 LLM 客户端获取可用模型。</summary>
    private async void OnFetchModelsClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiUrlEntry.Text) || string.IsNullOrWhiteSpace(ApiKeyEntry.Text))
        {
            FetchModelsResultLabel.Text = "请先填写 Base URL 和 API Key";
            FetchModelsResultLabel.IsVisible = true;
            return;
        }

        FetchModelsButton.IsEnabled = false;
        FetchModelsResultLabel.Text = "正在获取模型列表…";
        FetchModelsResultLabel.IsVisible = true;
        FetchedModels.Clear();

        try
        {
            ApplyTempConfigOverride();
            var models = await _llmClient.GetModelsAsync();
            if (models.Count == 0)
            {
                FetchModelsResultLabel.Text = "未获取到可用模型，请检查 API 权限";
                return;
            }

            foreach (var m in models)
                FetchedModels.Add(m);

            FetchModelsResultLabel.Text = $"共获取到 {models.Count} 个模型，请选择";
        }
        catch (Exception ex)
        {
            FetchModelsResultLabel.Text = $"获取失败：{ex.Message}";
        }
        finally
        {
            OpenAiCompatibleLlmClient.TempConfigOverride = null;
            FetchModelsButton.IsEnabled = true;
        }
    }

    /// <summary>点击「测试连接」按钮，临时保存配置后调用 LLM 客户端测试连通性。</summary>
    private async void OnTestConnectionClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiUrlEntry.Text) || string.IsNullOrWhiteSpace(ApiKeyEntry.Text))
        {
            TestResultLabel.Text = "请先填写 Base URL 和 API Key";
            TestResultLabel.TextColor = Application.Current?.Resources?["ErrorColor"] as Color ?? Colors.Red;
            return;
        }

        TestConnectionButton.IsEnabled = false;
        TestResultLabel.Text = "正在测试连接…";
        TestResultLabel.TextColor = Application.Current?.Resources?["TextSecondaryColor"] as Color ?? Colors.Gray;

        try
        {
            ApplyTempConfigOverride();
            var ok = await _llmClient.TestConnectionAsync();
            TestResultLabel.Text = ok ? "连接成功 ✓" : "连接失败，请检查配置";
            TestResultLabel.TextColor = ok
                ? (Application.Current?.Resources?["SuccessColor"] as Color ?? Colors.Green)
                : (Application.Current?.Resources?["ErrorColor"] as Color ?? Colors.Red);
        }
        catch (Exception ex)
        {
            TestResultLabel.Text = $"连接异常：{ex.Message}";
            TestResultLabel.TextColor = Application.Current?.Resources?["ErrorColor"] as Color ?? Colors.Red;
        }
        finally
        {
            OpenAiCompatibleLlmClient.TempConfigOverride = null;
            TestConnectionButton.IsEnabled = true;
        }
    }

    /// <summary>点击「保存」按钮，校验后构造 LlmConfig 并保存，随后返回上一页。</summary>
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var name = NameEntry.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("提示", "请填写配置名称", "确定");
            return;
        }

        if (!int.TryParse(MaxCompletionTokensEntry.Text?.Trim() ?? "0", out var maxCompletionTokens) || maxCompletionTokens < 0)
        {
            await DisplayAlert("提示", "最大输出 Tokens 必须为非负整数（0 表示使用默认）", "确定");
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            var provider = ProviderPicker.SelectedItem as LlmProviderInfo;
            var config = new LlmConfig
            {
                Name = name,
                Provider = provider?.Id ?? "custom",
                ApiUrl = ApiUrlEntry.Text?.Trim() ?? "",
                ApiKey = ApiKeyEntry.Text?.Trim() ?? "",
                Model = ModelEntry.Text?.Trim() ?? "",
                Enabled = EnabledSwitch.IsToggled,
                FallbackEnabled = FallbackSwitch.IsToggled,
                ReasoningEffort = ReasoningEffortPicker.SelectedItem as string ?? "disabled",
                TopP = TopPSlider.Value,
                FrequencyPenalty = FrequencyPenaltySlider.Value,
                PresencePenalty = PresencePenaltySlider.Value,
                ResponseFormat = ResponseFormatPicker.SelectedItem as string ?? "text",
                MaxCompletionTokens = maxCompletionTokens,
                ContextCaching = ContextCachingSwitch.IsToggled
            };
            AgentService.SaveConfig(config);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert("保存失败", ex.Message, "确定");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 将当前表单内容作为临时配置覆盖设置到 LlmClient（不写入持久化存储）。
    /// 仅在测试连接/获取模型时使用，调用方需在 finally 中将 TempConfigOverride 置空。
    /// </summary>
    private void ApplyTempConfigOverride()
    {
        var provider = ProviderPicker.SelectedItem as LlmProviderInfo;
        int.TryParse(MaxCompletionTokensEntry.Text?.Trim() ?? "0", out var maxCompletionTokens);
        OpenAiCompatibleLlmClient.TempConfigOverride = new LlmConfig
        {
            Name = string.IsNullOrWhiteSpace(NameEntry.Text) ? "__temp__" : NameEntry.Text.Trim(),
            Provider = provider?.Id ?? "custom",
            ApiUrl = ApiUrlEntry.Text?.Trim() ?? "",
            ApiKey = ApiKeyEntry.Text?.Trim() ?? "",
            Model = ModelEntry.Text?.Trim() ?? "",
            Enabled = true,
            FallbackEnabled = FallbackSwitch.IsToggled,
            ReasoningEffort = ReasoningEffortPicker.SelectedItem as string ?? "disabled",
            TopP = TopPSlider.Value,
            FrequencyPenalty = FrequencyPenaltySlider.Value,
            PresencePenalty = PresencePenaltySlider.Value,
            ResponseFormat = ResponseFormatPicker.SelectedItem as string ?? "text",
            MaxCompletionTokens = maxCompletionTokens > 0 ? maxCompletionTokens : 0,
            ContextCaching = ContextCachingSwitch.IsToggled
        };
    }
}
