using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 服务端连接设置页面 —— 配置 NAS CatClaw Server 地址
/// </summary>
public class ServerSettingsFragment : SettingsSubPageFragment
{
    protected override string GetTitle() => "CatClaw 服务端";

    private EditText? _serverUrlInput;
    private Button? _testButton;
    private Button? _syncButton;
    private TextView? _statusText;
    private ProgressBar? _progressBar;

    private ICatClawServerService? _serverService;
    private Android.Content.ISharedPreferences? _prefs;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        return inflater.Inflate(Resource.Layout.fragment_server_settings, container, false)!;
    }

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _serverUrlInput = view.FindViewById<EditText>(Resource.Id.server_url_input);
        _testButton = view.FindViewById<Button>(Resource.Id.test_connection_button);
        _syncButton = view.FindViewById<Button>(Resource.Id.sync_button);
        _statusText = view.FindViewById<TextView>(Resource.Id.server_status_text);
        _progressBar = view.FindViewById<ProgressBar>(Resource.Id.server_progress);

        _serverService = MainApplication.Services.GetRequiredService<ICatClawServerService>();

        // Restore saved server URL
        _prefs = Activity?.GetSharedPreferences("catclaw_server", Android.Content.FileCreationMode.Private);
        var savedUrl = _prefs?.GetString("server_url", "http://music.08102516.xyz:5000") ?? "http://music.08102516.xyz:5000";
        _serverUrlInput!.Text = savedUrl;
        _serverService!.ServerUrl = savedUrl;

        _testButton!.Click += async (s, e) =>
        {
            _testButton.Enabled = false;
            _statusText!.Text = "正在测试连接...";
            _progressBar!.Visibility = ViewStates.Visible;

            var url = _serverUrlInput.Text?.Trim() ?? "";
            _serverService.ServerUrl = url;

            // Save preference
            _prefs?.Edit()?.PutString("server_url", url)?.Apply();

            var ok = await _serverService.TestConnectionAsync();

            _statusText.Text = ok ? "✅ 连接成功" : "❌ 连接失败，请检查地址";
            _progressBar.Visibility = ViewStates.Gone;
            _testButton.Enabled = true;
        };

        _syncButton!.Click += async (s, e) =>
        {
            _syncButton.Enabled = false;
            _progressBar!.Visibility = ViewStates.Visible;

            var progress = new Progress<(string, int)>(report =>
            {
                Activity?.RunOnUiThread(() =>
                {
                    _statusText!.Text = $"{report.Item1} ({report.Item2}%)";
                });
            });

            var count = await _serverService!.SyncMetadataAsync(progress);

            Activity?.RunOnUiThread(() =>
            {
                _statusText!.Text = $"同步完成：新增 {count} 首歌曲";
                _progressBar!.Visibility = ViewStates.Gone;
                _syncButton.Enabled = true;
            });
        };
    }
}
