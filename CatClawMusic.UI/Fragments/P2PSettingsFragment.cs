using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// P2P 设置页面 —— 开关、限速、根服务器地址、设备列表
/// </summary>
public class P2PSettingsFragment : SettingsSubPageFragment
{
    protected override string GetTitle() => "P2P 设置";

    private SwitchCompat? _p2pSwitch;
    private SeekBar? _rateLimitSeekBar;
    private TextView? _rateLimitLabel;
    private EditText? _bootstrapInput;
    private EditText? _deviceNameInput;
    private Button? _discoverButton;
    private TextView? _devicesList;
    private TextView? _statusText;

    private IP2PService? _p2pService;
    private P2PConfig _config = new();
    private Android.Content.ISharedPreferences? _prefs;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        return inflater.Inflate(Resource.Layout.fragment_p2p_settings, container, false)!;
    }

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _p2pSwitch = view.FindViewById<SwitchCompat>(Resource.Id.p2p_switch);
        _rateLimitSeekBar = view.FindViewById<SeekBar>(Resource.Id.rate_limit_seekbar);
        _rateLimitLabel = view.FindViewById<TextView>(Resource.Id.rate_limit_label);
        _bootstrapInput = view.FindViewById<EditText>(Resource.Id.bootstrap_input);
        _deviceNameInput = view.FindViewById<EditText>(Resource.Id.device_name_input);
        _discoverButton = view.FindViewById<Button>(Resource.Id.discover_button);
        _devicesList = view.FindViewById<TextView>(Resource.Id.devices_list);
        _statusText = view.FindViewById<TextView>(Resource.Id.p2p_status_text);

        _p2pService = MainApplication.Services.GetRequiredService<IP2PService>();
        _config = _p2pService.Config;

        // Restore preferences
        _prefs = Activity?.GetSharedPreferences("catclaw_p2p", Android.Content.FileCreationMode.Private);
        _config.Enabled = _prefs?.GetBoolean("p2p_enabled", false) ?? false;
        _config.RateLimitKBs = _prefs?.GetInt("p2p_rate_limit", 128) ?? 128;
        _config.BootstrapNode = _prefs?.GetString("p2p_bootstrap", "music.08102516.xyz:6881") ?? "music.08102516.xyz:6881";
        _config.DeviceName = _prefs?.GetString("p2p_device_name", "CatClawApp") ?? "CatClawApp";

        // Bind UI
        _p2pSwitch!.Checked = _config.Enabled;
        _rateLimitSeekBar!.Progress = _config.RateLimitKBs / 8; // 0-1280 range (0-10240 KB/s)
        _rateLimitSeekBar.Max = 1280;
        UpdateRateLabel();
        _bootstrapInput!.Text = _config.BootstrapNode;
        _deviceNameInput!.Text = _config.DeviceName;
        UpdateStatusText();

        // Events
        _p2pSwitch.CheckedChange += async (s, e) =>
        {
            _config.Enabled = e.IsChecked;
            _prefs?.Edit()?.PutBoolean("p2p_enabled", _config.Enabled)?.Apply();
            UpdateStatusText();

            if (_config.Enabled)
                await _p2pService!.StartAsync();
            else
                await _p2pService!.StopAsync();
        };

        _rateLimitSeekBar.ProgressChanged += (s, e) =>
        {
            _config.RateLimitKBs = e.Progress * 8;
            if (_config.RateLimitKBs == 0) _config.RateLimitKBs = 128; // min 128KB/s
            _prefs?.Edit()?.PutInt("p2p_rate_limit", _config.RateLimitKBs)?.Apply();
            UpdateRateLabel();
        };

        _bootstrapInput!.TextChanged += (s, e) =>
        {
            _config.BootstrapNode = (_bootstrapInput.Text ?? "music.08102516.xyz:6881").Trim();
            _prefs?.Edit()?.PutString("p2p_bootstrap", _config.BootstrapNode)?.Apply();
        };

        _deviceNameInput!.TextChanged += (s, e) =>
        {
            _config.DeviceName = (_deviceNameInput.Text ?? "CatClawApp").Trim();
            _prefs?.Edit()?.PutString("p2p_device_name", _config.DeviceName)?.Apply();
        };

        _discoverButton!.Click += async (s, e) =>
        {
            _discoverButton.Enabled = false;
            _devicesList!.Text = "搜索中...";

            var devices = await _p2pService!.DiscoverDevicesAsync();

            if (devices.Count == 0)
            {
                _devicesList.Text = "未发现其他设备";
            }
            else
            {
                _devicesList.Text = string.Join("\n", devices.Select(d =>
                    $"📱 {d.Name}  ({d.Ip}:{d.HttpPort})  [{d.SongCount}首]"));
            }

            _discoverButton.Enabled = true;
        };
    }

    private void UpdateRateLabel()
    {
        _rateLimitLabel!.Text = _config.RateLimitKBs == 0
            ? "无限制"
            : $"{_config.RateLimitKBs} KB/s";
    }

    private void UpdateStatusText()
    {
        _statusText!.Text = _config.Enabled ? "🟢 P2P 已启用" : "⚫ P2P 已关闭";
    }
}
