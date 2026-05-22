using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SmbSettingsFragment : SettingsSubPageFragment
{
    private SmbSettingsViewModel _viewModel = null!;
    private EditText _etName = null!, _etHost = null!, _etPort = null!, _etUser = null!, _etPass = null!;
    private EditText _etShareName = null!, _etDomainName = null!, _etBasePath = null!;
    private Button _btnTest = null!, _btnSave = null!, _btnBrowse = null!;
    private TextView _statusText = null!;

    protected override string GetTitle() => "SMB 设置";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_smb_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<SmbSettingsViewModel>();

        _etName = view.FindViewById<EditText>(Resource.Id.et_name)!;
        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etPort = view.FindViewById<EditText>(Resource.Id.et_port)!;
        _etShareName = view.FindViewById<EditText>(Resource.Id.et_share_name)!;
        _etDomainName = view.FindViewById<EditText>(Resource.Id.et_domain_name)!;
        _etUser = view.FindViewById<EditText>(Resource.Id.et_username)!;
        _etPass = view.FindViewById<EditText>(Resource.Id.et_password)!;
        _etBasePath = view.FindViewById<EditText>(Resource.Id.et_base_path)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;
        _btnSave = view.FindViewById<Button>(Resource.Id.btn_save)!;
        _btnBrowse = view.FindViewById<Button>(Resource.Id.btn_browse)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _etName.TextChanged += (s, e) => _viewModel.Name = e?.Text?.ToString() ?? "";
        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etPort.TextChanged += (s, e) => _viewModel.Port = e?.Text?.ToString() ?? "";
        _etShareName.TextChanged += (s, e) => _viewModel.ShareName = e?.Text?.ToString() ?? "";
        _etDomainName.TextChanged += (s, e) => _viewModel.DomainName = e?.Text?.ToString() ?? "";
        _etUser.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _etPass.TextChanged += (s, e) => _viewModel.Password = e?.Text?.ToString() ?? "";
        _etBasePath.TextChanged += (s, e) => _viewModel.BasePath = e?.Text?.ToString() ?? "";
        _btnTest.Click += (s, e) => _viewModel.TestCommand.Execute(null);
        _btnSave.Click += (s, e) => _viewModel.SaveCommand.Execute(null);
        _btnBrowse.Click += OnBrowseClick;

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
                else if (e.PropertyName == nameof(_viewModel.Name) && _etName.Text != _viewModel.Name)
                    _etName.Text = _viewModel.Name;
                else if (e.PropertyName == nameof(_viewModel.Host) && _etHost.Text != _viewModel.Host)
                    _etHost.Text = _viewModel.Host;
                else if (e.PropertyName == nameof(_viewModel.Port) && _etPort.Text != _viewModel.Port)
                    _etPort.Text = _viewModel.Port;
                else if (e.PropertyName == nameof(_viewModel.ShareName) && _etShareName.Text != _viewModel.ShareName)
                    _etShareName.Text = _viewModel.ShareName;
                else if (e.PropertyName == nameof(_viewModel.DomainName) && _etDomainName.Text != _viewModel.DomainName)
                    _etDomainName.Text = _viewModel.DomainName;
                else if (e.PropertyName == nameof(_viewModel.UserName) && _etUser.Text != _viewModel.UserName)
                    _etUser.Text = _viewModel.UserName;
                else if (e.PropertyName == nameof(_viewModel.Password) && _etPass.Text != _viewModel.Password)
                    _etPass.Text = _viewModel.Password;
                else if (e.PropertyName == nameof(_viewModel.BasePath) && _etBasePath.Text != _viewModel.BasePath)
                    _etBasePath.Text = _viewModel.BasePath;
            });
        };

        _ = _viewModel.LoadAsync();
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        var activity = Activity;
        if (activity == null) return;

        if (string.IsNullOrWhiteSpace(_viewModel.Host))
        {
            _viewModel.StatusText = "请先输入主机地址";
            return;
        }
        if (string.IsNullOrWhiteSpace(_viewModel.ShareName))
        {
            _viewModel.StatusText = "请先输入共享名";
            return;
        }

        var profile = new ConnectionProfile
        {
            Host = _viewModel.Host.Trim(),
            Port = int.TryParse(_viewModel.Port, out var p) ? p : 445,
            UserName = _viewModel.UserName,
            Password = _viewModel.Password,
            DomainName = _viewModel.DomainName,
            ShareName = _viewModel.ShareName,
            BasePath = "\\",
            IsEnabled = true
        };

        var dialog = new SmbBrowserDialog(activity, profile);
        dialog.DismissEvent += (s, args) =>
        {
            if (!string.IsNullOrEmpty(dialog.SelectedPath))
            {
                _viewModel.BasePath = dialog.SelectedPath;
            }
        };
        dialog.Show();
    }
}
