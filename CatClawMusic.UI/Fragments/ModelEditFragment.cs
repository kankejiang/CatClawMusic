using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Services.AI;
using Microsoft.Extensions.DependencyInjection;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using System.Text.Json;
using System.Text;

namespace CatClawMusic.UI.Fragments;

public class ModelEditFragment : Fragment
{
    private LlmConfig? _editingConfig;
    private Spinner? _spinnerProvider;
    private Spinner? _spinnerModels;
    private EditText? _etName;
    private EditText? _etApiUrl;
    private EditText? _etApiKey;
    private EditText? _etModel;
    private SeekBar? _seekbarTemperature;
    private TextView? _tvTemperatureValue;
    private EditText? _etMaxTokens;
    private TextView? _tvTitle;
    private TextView? _tvStatus;
    private bool _isProviderSpinnerInitialized;
    private bool _isModelSpinnerInitialized;
    private List<string> _fetchedModels = new();
    private LlmProviderInfo[] _providers = Array.Empty<LlmProviderInfo>();

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_model_edit, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;
        _spinnerProvider = view.FindViewById<Spinner>(Resource.Id.spinner_provider)!;
        _spinnerModels = view.FindViewById<Spinner>(Resource.Id.spinner_models)!;
        _etName = view.FindViewById<EditText>(Resource.Id.et_name)!;
        _etApiUrl = view.FindViewById<EditText>(Resource.Id.et_api_url)!;
        _etApiKey = view.FindViewById<EditText>(Resource.Id.et_api_key)!;
        _etModel = view.FindViewById<EditText>(Resource.Id.et_model)!;
        _seekbarTemperature = view.FindViewById<SeekBar>(Resource.Id.seekbar_temperature)!;
        _tvTemperatureValue = view.FindViewById<TextView>(Resource.Id.tv_temperature_value)!;
        _etMaxTokens = view.FindViewById<EditText>(Resource.Id.et_max_tokens)!;
        _tvStatus = view.FindViewById<TextView>(Resource.Id.tv_status)!;

        _providers = AgentService.GetProviders();

        if (Arguments != null && Arguments.ContainsKey("modelName"))
        {
            var modelName = Arguments.GetString("modelName");
            var allConfigs = AgentService.LoadAllConfigs();
            _editingConfig = allConfigs.FirstOrDefault(c => c.Name == modelName);
            _tvTitle.Text = "编辑模型";
            LoadConfigToUi(_editingConfig);
        }
        else if (Arguments != null && !string.IsNullOrEmpty(Arguments.GetString("provider")))
        {
            _editingConfig = new LlmConfig
            {
                Name = Arguments.GetString("modelName") ?? "",
                Provider = Arguments.GetString("provider") ?? "",
                ApiUrl = Arguments.GetString("apiUrl") ?? "",
                ApiKey = Arguments.GetString("apiKey") ?? "",
                Model = Arguments.GetString("modelId") ?? "",
                Temperature = Arguments.GetDouble("temperature"),
                MaxTokens = Arguments.GetInt("maxTokens"),
                Enabled = Arguments.GetBoolean("enabled")
            };
            _tvTitle.Text = "编辑模型";
            LoadConfigToUi(_editingConfig);
        }
        else
        {
            _tvTitle.Text = "添加模型";
        }

        SetupProviderSpinner();
        SetupModelSpinner();
        SetupTemperature();

        var btnSave = view.FindViewById<View>(Resource.Id.btn_save);
        if (btnSave != null)
            btnSave.Click += (s, e) => SaveConfig();

        var btnFetchModels = view.FindViewById<View>(Resource.Id.btn_fetch_models);
        if (btnFetchModels != null)
            btnFetchModels.Click += (s, e) => _ = FetchModelsAsync();

        var btnTestConnection = view.FindViewById<View>(Resource.Id.btn_test_connection);
        if (btnTestConnection != null)
            btnTestConnection.Click += (s, e) => _ = TestConnectionAsync();
    }

    private void LoadConfigToUi(LlmConfig? config)
    {
        if (config == null) return;

        _etName?.Post(() => { if (_etName != null) _etName.Text = config.Name; });
        _etApiUrl?.Post(() => { if (_etApiUrl != null) _etApiUrl.Text = config.ApiUrl; });
        _etApiKey?.Post(() => { if (_etApiKey != null) _etApiKey.Text = config.ApiKey; });
        _etModel?.Post(() => { if (_etModel != null) _etModel.Text = config.Model; });
        _seekbarTemperature?.Post(() => { if (_seekbarTemperature != null) _seekbarTemperature.Progress = (int)(config.Temperature * 100); });
        _tvTemperatureValue?.Post(() => { if (_tvTemperatureValue != null) _tvTemperatureValue.Text = config.Temperature.ToString("F1"); });
        _etMaxTokens?.Post(() => { if (_etMaxTokens != null) _etMaxTokens.Text = config.MaxTokens.ToString(); });

        var index = Array.FindIndex(_providers, p => p.Id == config.Provider);
        if (index >= 0)
            _spinnerProvider?.Post(() => { if (_spinnerProvider != null) _spinnerProvider.SetSelection(index, false); });
    }

    private void SetupProviderSpinner()
    {
        if (_spinnerProvider == null) return;

        var names = _providers.Select(p => p.Name).ToList();
        var adapter = new ArrayAdapter<string>(Context!, Android.Resource.Layout.SimpleSpinnerItem, names);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _spinnerProvider.Adapter = adapter;

        _spinnerProvider.ItemSelected += (s, e) =>
        {
            if (!_isProviderSpinnerInitialized)
            {
                _isProviderSpinnerInitialized = true;
                return;
            }
            var provider = _providers[e.Position];
            if (_etApiUrl != null)
                _etApiUrl.Text = provider.DefaultApiUrl;
            if (_etModel != null)
                _etModel.Text = "";
            _fetchedModels.Clear();
            UpdateModelSpinner();
            AutoFillName();
        };
    }

    private void SetupModelSpinner()
    {
        if (_spinnerModels == null) return;

        var placeholder = new List<string> { "选择模型..." };
        var adapter = new ArrayAdapter<string>(Context!, Android.Resource.Layout.SimpleSpinnerItem, placeholder);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _spinnerModels.Adapter = adapter;

        _spinnerModels.ItemSelected += (s, e) =>
        {
            if (!_isModelSpinnerInitialized)
            {
                _isModelSpinnerInitialized = true;
                return;
            }
            if (e.Position > 0 && e.Position <= _fetchedModels.Count)
            {
                var selected = _fetchedModels[e.Position - 1];
                if (_etModel != null)
                    _etModel.Text = selected;
                AutoFillName();
            }
        };
    }

    private void UpdateModelSpinner()
    {
        if (_spinnerModels == null || Context == null) return;

        var items = new List<string> { "选择模型..." };
        items.AddRange(_fetchedModels);
        var adapter = new ArrayAdapter<string>(Context, Android.Resource.Layout.SimpleSpinnerItem, items);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _spinnerModels.Adapter = adapter;
        _isModelSpinnerInitialized = false;
    }

    private async Task FetchModelsAsync()
    {
        var apiUrl = _etApiUrl?.Text?.Trim() ?? "";
        var apiKey = _etApiKey?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            ShowStatus("请先输入 API 地址");
            return;
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowStatus("请先输入 API Key");
            return;
        }

        var providerIndex = _spinnerProvider?.SelectedItemPosition ?? 0;
        var provider = _providers[Math.Min(providerIndex, _providers.Length - 1)];

        ShowStatus("正在获取模型列表...");

        try
        {
            var modelsUrl = BuildModelsUrl(apiUrl);
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, modelsUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var presetModels = provider.PresetModels?.ToList() ?? new List<string>();
                if (presetModels.Count > 0)
                {
                    _fetchedModels = presetModels;
                    UpdateModelSpinner();
                    ShowStatus($"API 不支持获取模型列表，已加载 {presetModels.Count} 个预设模型");
                    return;
                }
                ShowStatus($"获取失败 ({(int)response.StatusCode})");
                return;
            }

            _fetchedModels = ParseModels(body);
            if (_fetchedModels.Count == 0)
            {
                var presetModels = provider.PresetModels?.ToList() ?? new List<string>();
                if (presetModels.Count > 0)
                {
                    _fetchedModels = presetModels;
                    UpdateModelSpinner();
                    ShowStatus($"已加载 {presetModels.Count} 个预设模型");
                    return;
                }
                ShowStatus("未获取到模型");
                return;
            }

            UpdateModelSpinner();
            ShowStatus($"获取成功，共 {_fetchedModels.Count} 个模型");
        }
        catch (Exception ex)
        {
            var presetModels = provider.PresetModels?.ToList() ?? new List<string>();
            if (presetModels.Count > 0)
            {
                _fetchedModels = presetModels;
                UpdateModelSpinner();
                ShowStatus($"获取失败，已加载 {presetModels.Count} 个预设模型");
                return;
            }
            ShowStatus($"获取失败: {ex.Message}");
        }
    }

    private static string BuildModelsUrl(string apiUrl)
    {
        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/chat/completions".Length];
        if (url.Contains("/compatible-mode/", StringComparison.OrdinalIgnoreCase))
            url = url[..url.IndexOf("/compatible-mode/", StringComparison.OrdinalIgnoreCase)] + "/api/v1/models";
        else if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) || url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            url = url.TrimEnd('/') + "/models";
        else
            url = url + "/v1/models";
        return url;
    }

    private async Task TestConnectionAsync()
    {
        var apiUrl = _etApiUrl?.Text?.Trim() ?? "";
        var apiKey = _etApiKey?.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            ShowStatus("请先输入 API 地址");
            return;
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowStatus("请先输入 API Key");
            return;
        }

        ShowStatus("正在测试连接...");

        try
        {
            var chatUrl = BuildChatUrl(apiUrl);
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var testBody = $"{{\"model\":\"{_etModel?.Text?.Trim() ?? "test"}\",\"messages\":[{{\"role\":\"user\",\"content\":\"Hi\"}}],\"max_tokens\":5}}";
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, chatUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new System.Net.Http.StringContent(testBody, Encoding.UTF8, "application/json");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await client.SendAsync(request);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                ShowStatus($"连接成功！延迟 {sw.ElapsedMilliseconds}ms");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                ShowStatus($"连接失败 ({(int)response.StatusCode}): {Truncate(body, 100)}");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"连接失败: {ex.Message}");
        }
    }

    private static string BuildChatUrl(string apiUrl)
    {
        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return url + "/chat/completions";
        if (url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            return url + "chat/completions";
        return url + "/v1/chat/completions";
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    private static List<string> ParseModels(string body)
    {
        var models = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        var idStr = id.GetString();
                        if (!string.IsNullOrEmpty(idStr))
                            models.Add(idStr);
                    }
                }
            }
            models.Sort((a, b) => string.Compare(a, b, StringComparison.Ordinal));
        }
        catch { }
        return models;
    }

    private void AutoFillName()
    {
        if (_editingConfig != null) return;
        var providerIndex = _spinnerProvider?.SelectedItemPosition ?? 0;
        var provider = _providers[Math.Min(providerIndex, _providers.Length - 1)];
        var model = _etModel?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(model) && _etName != null)
            _etName.Text = $"{provider.Name}-{model}";
    }

    private void SetupTemperature()
    {
        if (_seekbarTemperature != null)
        {
            _seekbarTemperature.ProgressChanged += (s, e) =>
            {
                var temp = _seekbarTemperature.Progress / 100.0;
                _tvTemperatureValue?.Post(() => { if (_tvTemperatureValue != null) _tvTemperatureValue.Text = temp.ToString("F1"); });
            };
        }
    }

    private void SaveConfig()
    {
        if (string.IsNullOrWhiteSpace(_etName?.Text))
        {
            ShowStatus("请输入配置名称");
            return;
        }

        var providerIndex = _spinnerProvider?.SelectedItemPosition ?? 0;
        var provider = _providers[Math.Min(providerIndex, _providers.Length - 1)];

        var config = new LlmConfig
        {
            Name = _etName?.Text?.Trim() ?? "新配置",
            Provider = provider.Id,
            ApiUrl = _etApiUrl?.Text?.Trim() ?? "",
            ApiKey = _etApiKey?.Text?.Trim() ?? "",
            Model = _etModel?.Text?.Trim() ?? "",
            Temperature = (_seekbarTemperature?.Progress ?? 70) / 100.0,
            MaxTokens = int.TryParse(_etMaxTokens?.Text, out var mt) ? mt : 2048,
            Enabled = _editingConfig?.Enabled ?? true
        };

        if (_editingConfig != null)
        {
            config.Enabled = _editingConfig.Enabled;
            if (_editingConfig.Name != config.Name)
            {
                AgentService.DeleteConfig(_editingConfig.Name);
            }
        }

        AgentService.SaveConfig(config);
        AgentService.SetCurrentConfigName(config.Name);
        ShowStatus("保存成功");

        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
        {
            Activity?.RunOnUiThread(() => MainApplication.Services.GetRequiredService<INavigationService>().GoBack());
        });
    }

    private void ShowStatus(string message)
    {
        _tvStatus?.Post(() =>
        {
            if (_tvStatus != null)
            {
                _tvStatus.Text = message;
                _tvStatus.Visibility = ViewStates.Visible;
            }
        });
    }
}
