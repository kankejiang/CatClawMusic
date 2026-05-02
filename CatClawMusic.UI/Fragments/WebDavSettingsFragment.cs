using Android.Views;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class WebDavSettingsFragment : SettingsSubPageFragment
{
    private WebDavSettingsViewModel _viewModel = null!;
    private EditText _etName = null!, _etHost = null!, _etPort = null!, _etUser = null!, _etPass = null!, _etBasePath = null!;
    private Button _btnTest = null!, _btnSave = null!;
    private TextView _statusText = null!;
    private AndroidX.AppCompat.Widget.SwitchCompat _swHttps = null!;

    protected override string GetTitle() => "WebDAV 设置";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_webdav_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<WebDavSettingsViewModel>();

        _etName = view.FindViewById<EditText>(Resource.Id.et_name)!;
        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etPort = view.FindViewById<EditText>(Resource.Id.et_port)!;
        _etUser = view.FindViewById<EditText>(Resource.Id.et_username)!;
        _etPass = view.FindViewById<EditText>(Resource.Id.et_password)!;
        _etBasePath = view.FindViewById<EditText>(Resource.Id.et_base_path)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;
        _btnSave = view.FindViewById<Button>(Resource.Id.btn_save)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;
        _swHttps = view.FindViewById<AndroidX.AppCompat.Widget.SwitchCompat>(Resource.Id.sw_https);

        _etName.TextChanged += (s, e) => _viewModel.Name = e?.Text?.ToString() ?? "";
        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etPort.TextChanged += (s, e) => _viewModel.Port = e?.Text?.ToString() ?? "";
        _etUser.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _etPass.TextChanged += (s, e) => _viewModel.Password = e?.Text?.ToString() ?? "";
        _etBasePath.TextChanged += (s, e) => _viewModel.BasePath = e?.Text?.ToString() ?? "";
        if (_swHttps != null)
            _swHttps.CheckedChange += (s, e) => _viewModel.UseHttps = e.IsChecked;
        _btnTest.Click += (s, e) => _viewModel.TestCommand.Execute(null);
        _btnSave.Click += (s, e) => _viewModel.SaveCommand.Execute(null);

        _viewModel.PropertyChanged += (s, e) =>
        {
            var a = Activity;
            if (a == null) return;
            a.RunOnUiThread(() =>
            {
                if (e.PropertyName == nameof(_viewModel.StatusText))
                {
                    _statusText.Visibility = string.IsNullOrEmpty(_viewModel.StatusText)
                        ? ViewStates.Gone : ViewStates.Visible;
                    _statusText.Text = _viewModel.StatusText;
                }
                // 从 DB 加载后推送到 UI（仅在值不同时更新，避免光标重置）
                else if (e.PropertyName == nameof(_viewModel.Name) && _etName.Text != _viewModel.Name)
                    _etName.Text = _viewModel.Name;
                else if (e.PropertyName == nameof(_viewModel.Host) && _etHost.Text != _viewModel.Host)
                    _etHost.Text = _viewModel.Host;
                else if (e.PropertyName == nameof(_viewModel.Port) && _etPort.Text != _viewModel.Port)
                    _etPort.Text = _viewModel.Port;
                else if (e.PropertyName == nameof(_viewModel.UserName) && _etUser.Text != _viewModel.UserName)
                    _etUser.Text = _viewModel.UserName;
                else if (e.PropertyName == nameof(_viewModel.Password) && _etPass.Text != _viewModel.Password)
                    _etPass.Text = _viewModel.Password;
                else if (e.PropertyName == nameof(_viewModel.BasePath) && _etBasePath.Text != _viewModel.BasePath)
                    _etBasePath.Text = _viewModel.BasePath;
                else if (e.PropertyName == nameof(_viewModel.UseHttps) && _swHttps != null)
                    _swHttps.Checked = _viewModel.UseHttps;
            });
        };

        // 加载已保存配置（放在最后，此时所有 UI 绑定已完成）
        _ = _viewModel.LoadAsync();
    }
}
