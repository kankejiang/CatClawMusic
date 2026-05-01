using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class WebDavSettingsFragment : SettingsSubPageFragment
{
    private WebDavSettingsViewModel _viewModel = null!;
    private EditText _etHost = null!, _etPort = null!, _etUser = null!, _etPass = null!, _etBasePath = null!;
    private Button _btnTest = null!, _btnSave = null!;
    private TextView _statusText = null!;

    protected override string GetTitle() => "WebDAV 设置";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_webdav_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<WebDavSettingsViewModel>();

        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etPort = view.FindViewById<EditText>(Resource.Id.et_port)!;
        _etUser = view.FindViewById<EditText>(Resource.Id.et_username)!;
        _etPass = view.FindViewById<EditText>(Resource.Id.et_password)!;
        _etBasePath = view.FindViewById<EditText>(Resource.Id.et_base_path)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;
        _btnSave = view.FindViewById<Button>(Resource.Id.btn_save)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etPort.TextChanged += (s, e) => _viewModel.Port = e?.Text?.ToString() ?? "";
        _etUser.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _etPass.TextChanged += (s, e) => _viewModel.Password = e?.Text?.ToString() ?? "";
        _etBasePath.TextChanged += (s, e) => _viewModel.BasePath = e?.Text?.ToString() ?? "";
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
            });
        };
    }
}
