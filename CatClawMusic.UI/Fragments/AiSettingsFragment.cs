using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Services.AI;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class AiSettingsFragment : Fragment
{
    private Switch? _switchEnabled;
    private Spinner? _spinnerProvider;
    private EditText? _etApiUrl;
    private EditText? _etApiKey;
    private EditText? _etModel;
    private SeekBar? _seekBarTemperature;
    private TextView? _tvTemperatureValue;
    private EditText? _etMaxTokens;
    private TextView? _tvStatus;
    private bool _isProviderSpinnerInitialized;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_ai_settings, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _switchEnabled = view.FindViewById<Switch>(Resource.Id.switch_ai_enabled);
        _spinnerProvider = view.FindViewById<Spinner>(Resource.Id.spinner_provider);
        _etApiUrl = view.FindViewById<EditText>(Resource.Id.et_api_url);
        _etApiKey = view.FindViewById<EditText>(Resource.Id.et_api_key);
        _etModel = view.FindViewById<EditText>(Resource.Id.et_model);
        _seekBarTemperature = view.FindViewById<SeekBar>(Resource.Id.seekbar_temperature);
        _tvTemperatureValue = view.FindViewById<TextView>(Resource.Id.tv_temperature_value);
        _etMaxTokens = view.FindViewById<EditText>(Resource.Id.et_max_tokens);
        _tvStatus = view.FindViewById<TextView>(Resource.Id.tv_status);

        SetupProviderSpinner();
        LoadConfig();

        if (_seekBarTemperature != null)
        {
            _seekBarTemperature.ProgressChanged += (s, e) =>
            {
                var temp = _seekBarTemperature.Progress / 100.0;
                _tvTemperatureValue?.Post(() => _tvTemperatureValue.Text = temp.ToString("F1"));
            };
        }

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

    private void LoadConfig()
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

    private LlmConfig BuildConfigFromUi()
    {
        var providers = AgentService.GetProviders();
        var providerIdx = _spinnerProvider?.SelectedItemPosition ?? 0;
        var provider = providers[Math.Min(providerIdx, providers.Length - 1)];

        return new LlmConfig
        {
            Provider = provider.Id,
            ApiUrl = _etApiUrl?.Text?.Trim() ?? "",
            ApiKey = _etApiKey?.Text?.Trim() ?? "",
            Model = _etModel?.Text?.Trim() ?? provider.DefaultModel,
            Temperature = (_seekBarTemperature?.Progress ?? 70) / 100.0,
            MaxTokens = int.TryParse(_etMaxTokens?.Text, out var mt) ? mt : 2048,
            Enabled = _switchEnabled?.Checked ?? false
        };
    }

    private void SaveCurrentConfig()
    {
        var config = BuildConfigFromUi();
        AgentService.SaveConfig(config);
        ShowStatus("✅ 配置已保存");
        Toast.MakeText(Context, "AI 配置已保存", ToastLength.Short)?.Show();
    }

    private async Task TestConnectionAsync()
    {
        var config = BuildConfigFromUi();
        if (string.IsNullOrWhiteSpace(config.ApiUrl) || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            ShowStatus("❌ 请先填写 API 地址和 Key");
            return;
        }

        ShowStatus("🔄 正在测试连接...");
        var client = new OpenAiCompatibleLlmClient(() => config);
        var success = await client.TestConnectionAsync();

        if (success)
            ShowStatus("✅ 连接成功！AI 服务可用");
        else
            ShowStatus("❌ 连接失败，请检查 API 地址和 Key");
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
