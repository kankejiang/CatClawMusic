using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Services.AI;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class AiSettingsFragment : Fragment
{
    private Switch? _switchEnabled;
    private Spinner? _spinnerProvider;
    private Spinner? _spinnerModel;
    private EditText? _etConfigName;
    private EditText? _etApiUrl;
    private EditText? _etApiKey;
    private EditText? _etModel;
    private SeekBar? _seekBarTemperature;
    private TextView? _tvTemperatureValue;
    private EditText? _etMaxTokens;
    private TextView? _tvStatus;
    private TextView? _tvConfigCount;
    private TextView? _tvNoConfigs;
    private RecyclerView? _rvConfigEntries;
    private ConfigEntryAdapter? _configAdapter;
    private bool _isProviderSpinnerInitialized;
    private bool _isModelSpinnerInitialized;
    private string? _editingEntryId;
    private List<string> _fetchedModels = new();

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_ai_settings, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _switchEnabled = view.FindViewById<Switch>(Resource.Id.switch_ai_enabled);
        _spinnerProvider = view.FindViewById<Spinner>(Resource.Id.spinner_provider);
        _spinnerModel = view.FindViewById<Spinner>(Resource.Id.spinner_model);
        _etConfigName = view.FindViewById<EditText>(Resource.Id.et_config_name);
        _etApiUrl = view.FindViewById<EditText>(Resource.Id.et_api_url);
        _etApiKey = view.FindViewById<EditText>(Resource.Id.et_api_key);
        _etModel = view.FindViewById<EditText>(Resource.Id.et_model);
        _seekBarTemperature = view.FindViewById<SeekBar>(Resource.Id.seekbar_temperature);
        _tvTemperatureValue = view.FindViewById<TextView>(Resource.Id.tv_temperature_value);
        _etMaxTokens = view.FindViewById<EditText>(Resource.Id.et_max_tokens);
        _tvStatus = view.FindViewById<TextView>(Resource.Id.tv_status);
        _tvConfigCount = view.FindViewById<TextView>(Resource.Id.tv_config_count);
        _tvNoConfigs = view.FindViewById<TextView>(Resource.Id.tv_no_configs);
        _rvConfigEntries = view.FindViewById<RecyclerView>(Resource.Id.rv_config_entries);

        SetupProviderSpinner();
        SetupModelSpinner();
        SetupConfigList();
        LoadConfig();

        if (_seekBarTemperature != null)
        {
            _seekBarTemperature.ProgressChanged += (s, e) =>
            {
                var temp = _seekBarTemperature.Progress / 100.0;
                _tvTemperatureValue?.Post(() => _tvTemperatureValue.Text = temp.ToString("F1"));
            };
        }

        var btnFetchModels = view.FindViewById<View>(Resource.Id.btn_fetch_models);
        if (btnFetchModels != null)
            btnFetchModels.Click += (s, e) => _ = FetchModelsAsync();

        var btnTest = view.FindViewById<View>(Resource.Id.btn_test_connection);
        if (btnTest != null)
            btnTest.Click += (s, e) => _ = TestConnectionAsync();

        var btnSave = view.FindViewById<View>(Resource.Id.btn_save_config);
        if (btnSave != null)
            btnSave.Click += (s, e) => SaveCurrentConfig();
    }

    private void SetupProviderSpinner()
    {
        if (_spinnerProvider == null) return;

        var providers = AgentService.GetProviders();
        var names = providers.Select(p => p.Name).ToList();
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
            var provider = providers[e.Position];
            if (_etApiUrl != null && !string.IsNullOrEmpty(provider.DefaultApiUrl))
                _etApiUrl.Text = provider.DefaultApiUrl;
            if (_etModel != null && !string.IsNullOrEmpty(provider.DefaultModel))
                _etModel.Text = provider.DefaultModel;
        };
    }

    private void SetupModelSpinner()
    {
        if (_spinnerModel == null) return;

        _spinnerModel.ItemSelected += (s, e) =>
        {
            if (!_isModelSpinnerInitialized)
            {
                _isModelSpinnerInitialized = true;
                return;
            }
            if (e.Position >= 0 && e.Position < _fetchedModels.Count)
            {
                var selected = _fetchedModels[e.Position];
                _etModel?.Post(() => { if (_etModel != null) _etModel.Text = selected; });
            }
        };
    }

    private async Task FetchModelsAsync()
    {
        var config = BuildEntryFromUi();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            ShowStatus("请先填写 API 地址和 Key");
            return;
        }

        ShowStatus("正在获取模型列表...");

        try
        {
            var client = new OpenAiCompatibleLlmClient(() => config);
            var models = await client.GetModelsAsync();

            if (models.Count == 0)
            {
                ShowStatus("未获取到可用模型，请手动输入");
                return;
            }

            _fetchedModels = models;
            var adapter = new ArrayAdapter<string>(Context!, Android.Resource.Layout.SimpleSpinnerItem, models);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);

            _spinnerModel?.Post(() =>
            {
                if (_spinnerModel == null) return;
                _spinnerModel.Adapter = adapter;
                _spinnerModel.Visibility = ViewStates.Visible;

                var currentModel = _etModel?.Text?.Trim() ?? "";
                var idx = _fetchedModels.IndexOf(currentModel);
                if (idx < 0) idx = 0;
                _isModelSpinnerInitialized = false;
                _spinnerModel.SetSelection(idx);
            });

            _etModel?.Post(() => { if (_etModel != null) _etModel.Visibility = ViewStates.Gone; });

            ShowStatus($"获取到 {models.Count} 个可用模型");
        }
        catch (Exception ex)
        {
            ShowStatus($"获取失败: {ex.Message}");
        }
    }

    private void SetupConfigList()
    {
        if (_rvConfigEntries == null) return;

        _configAdapter = new ConfigEntryAdapter();
        _rvConfigEntries.SetLayoutManager(new LinearLayoutManager(Context));
        _rvConfigEntries.SetAdapter(_configAdapter);

        _configAdapter.OnEntryClick += entryId =>
        {
            var entries = AgentService.LoadAllConfigEntries();
            var entry = entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return;

            _editingEntryId = entry.Id;
            FillUiFromEntry(entry);
        };

        _configAdapter.OnEntryEnabledChanged += (entryId, enabled) =>
        {
            AgentService.ToggleConfigEntryEnabled(entryId, enabled);
            RefreshConfigList();
        };

        _configAdapter.OnEntryDelete += entryId =>
        {
            AgentService.DeleteConfigEntry(entryId);
            if (_editingEntryId == entryId)
                _editingEntryId = null;
            RefreshConfigList();
            ShowStatus("配置已删除");
        };

        RefreshConfigList();
    }

    private void FillUiFromEntry(LlmConfigEntry entry)
    {
        _etConfigName?.Post(() => { if (_etConfigName != null) _etConfigName.Text = entry.Name; });

        var providers = AgentService.GetProviders();
        var providerIdx = Array.FindIndex(providers, p => p.Id == entry.Provider);
        if (providerIdx < 0) providerIdx = 0;
        _spinnerProvider?.Post(() => { if (_spinnerProvider != null) _spinnerProvider.SetSelection(providerIdx, false); });

        _etApiUrl?.Post(() => { if (_etApiUrl != null) _etApiUrl.Text = entry.ApiUrl; });
        _etApiKey?.Post(() => { if (_etApiKey != null) _etApiKey.Text = entry.ApiKey; });
        _etModel?.Post(() => { if (_etModel != null) _etModel.Text = entry.Model; });
        _seekBarTemperature?.Post(() => { if (_seekBarTemperature != null) _seekBarTemperature.Progress = (int)(entry.Temperature * 100); });
        _tvTemperatureValue?.Post(() => { if (_tvTemperatureValue != null) _tvTemperatureValue.Text = entry.Temperature.ToString("F1"); });
        _etMaxTokens?.Post(() => { if (_etMaxTokens != null) _etMaxTokens.Text = entry.MaxTokens.ToString(); });

        _spinnerModel?.Post(() => { if (_spinnerModel != null) _spinnerModel.Visibility = ViewStates.Gone; });
        _etModel?.Post(() => { if (_etModel != null) _etModel.Visibility = ViewStates.Visible; });
        _fetchedModels.Clear();
    }

    private void RefreshConfigList()
    {
        var entries = AgentService.LoadAllConfigEntries();
        _configAdapter?.SetEntries(entries);

        if (_tvNoConfigs != null)
            _tvNoConfigs.Visibility = entries.Count == 0 ? ViewStates.Visible : ViewStates.Gone;

        if (_tvConfigCount != null)
            _tvConfigCount.Text = entries.Count > 0 ? $"{entries.Count} 个配置" : "";
    }

    private void LoadConfig()
    {
        var entries = AgentService.LoadAllConfigEntries();
        var activeEntry = entries.FirstOrDefault(e => e.IsActive);

        if (activeEntry != null)
        {
            _editingEntryId = activeEntry.Id;
            FillUiFromEntry(activeEntry);
            _switchEnabled?.Post(() => { if (_switchEnabled != null) _switchEnabled.Checked = activeEntry.Enabled; });
        }
        else
        {
            var config = AgentService.LoadConfig();
            _switchEnabled?.Post(() => { if (_switchEnabled != null) _switchEnabled.Checked = config.Enabled; });

            var providers = AgentService.GetProviders();
            var providerIdx = Array.FindIndex(providers, p => p.Id == config.Provider);
            if (providerIdx < 0) providerIdx = 0;
            _spinnerProvider?.Post(() => { if (_spinnerProvider != null) _spinnerProvider.SetSelection(providerIdx, false); });

            _etApiUrl?.Post(() => { if (_etApiUrl != null) _etApiUrl.Text = config.ApiUrl; });
            _etApiKey?.Post(() => { if (_etApiKey != null) _etApiKey.Text = config.ApiKey; });
            _etModel?.Post(() => { if (_etModel != null) _etModel.Text = config.Model; });
            _seekBarTemperature?.Post(() => { if (_seekBarTemperature != null) _seekBarTemperature.Progress = (int)(config.Temperature * 100); });
            _tvTemperatureValue?.Post(() => { if (_tvTemperatureValue != null) _tvTemperatureValue.Text = config.Temperature.ToString("F1"); });
            _etMaxTokens?.Post(() => { if (_etMaxTokens != null) _etMaxTokens.Text = config.MaxTokens.ToString(); });
        }
    }

    private LlmConfigEntry BuildEntryFromUi()
    {
        var providers = AgentService.GetProviders();
        var providerIdx = _spinnerProvider?.SelectedItemPosition ?? 0;
        var provider = providers[Math.Min(providerIdx, providers.Length - 1)];

        return new LlmConfigEntry
        {
            Id = _editingEntryId ?? Guid.NewGuid().ToString("N")[..8],
            Name = _etConfigName?.Text?.Trim() ?? "",
            Provider = provider.Id,
            ApiUrl = _etApiUrl?.Text?.Trim() ?? "",
            ApiKey = _etApiKey?.Text?.Trim() ?? "",
            Model = _etModel?.Text?.Trim() ?? provider.DefaultModel,
            Temperature = (_seekBarTemperature?.Progress ?? 70) / 100.0,
            MaxTokens = int.TryParse(_etMaxTokens?.Text, out var mt) ? mt : 2048,
            Enabled = _switchEnabled?.Checked ?? false,
            IsActive = true
        };
    }

    private void SaveCurrentConfig()
    {
        var entry = BuildEntryFromUi();

        if (string.IsNullOrWhiteSpace(entry.ApiUrl) || string.IsNullOrWhiteSpace(entry.ApiKey))
        {
            ShowStatus("请填写 API 地址和 Key");
            return;
        }

        AgentService.SaveConfigEntry(entry);
        AgentService.SaveConfig(entry);

        _editingEntryId = entry.Id;
        RefreshConfigList();
        ShowStatus("配置已保存并激活");
        Toast.MakeText(Context, "AI 配置已保存", ToastLength.Short)?.Show();
    }

    private async Task TestConnectionAsync()
    {
        var config = BuildEntryFromUi();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            ShowStatus("请先填写 API 地址和 Key");
            return;
        }

        ShowStatus("正在测试连接...");
        var client = new OpenAiCompatibleLlmClient(() => config);
        var success = await client.TestConnectionAsync();

        if (success)
            ShowStatus("连接成功！AI 服务可用");
        else
            ShowStatus("连接失败，请检查 API 地址和 Key");
    }

    private void ShowStatus(string text)
    {
        _tvStatus?.Post(() =>
        {
            if (_tvStatus != null)
            {
                _tvStatus.Text = text;
                _tvStatus.Visibility = ViewStates.Visible;
            }
        });
    }
}
