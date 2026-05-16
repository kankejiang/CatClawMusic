using Android.Views;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// WebDAV设置Fragment，配置WebDAV服务器连接参数
/// </summary>
public class WebDavSettingsFragment : SettingsSubPageFragment
{
    private WebDavSettingsViewModel _viewModel = null!;
    private EditText _etName = null!, _etHost = null!, _etPort = null!, _etUser = null!, _etPass = null!;
    private TextView _tvBasePath = null!;
    private Button _btnTest = null!, _btnSave = null!, _btnBrowse = null!;
    private TextView _statusText = null!;
    private AndroidX.AppCompat.Widget.SwitchCompat _swHttps = null!;

    protected override string GetTitle() => "WebDAV 设置";

    /// <summary>
    /// 创建WebDAV设置视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_webdav_settings, container, false)!;

    /// <summary>
    /// 子视图创建完成后初始化控件，绑定ViewModel和事件处理器
    /// </summary>
    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<WebDavSettingsViewModel>();

        _etName = view.FindViewById<EditText>(Resource.Id.et_name)!;
        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etPort = view.FindViewById<EditText>(Resource.Id.et_port)!;
        _etUser = view.FindViewById<EditText>(Resource.Id.et_username)!;
        _etPass = view.FindViewById<EditText>(Resource.Id.et_password)!;
        _tvBasePath = view.FindViewById<TextView>(Resource.Id.tv_base_path)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;
        _btnSave = view.FindViewById<Button>(Resource.Id.btn_save)!;
        _btnBrowse = view.FindViewById<Button>(Resource.Id.btn_browse)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;
        _swHttps = view.FindViewById<AndroidX.AppCompat.Widget.SwitchCompat>(Resource.Id.sw_https);

        _etName.TextChanged += (s, e) => _viewModel.Name = e?.Text?.ToString() ?? "";
        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etPort.TextChanged += (s, e) => _viewModel.Port = e?.Text?.ToString() ?? "";
        _etUser.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _etPass.TextChanged += (s, e) => _viewModel.Password = e?.Text?.ToString() ?? "";
        if (_swHttps != null)
            _swHttps.CheckedChange += (s, e) => _viewModel.UseHttps = e.IsChecked;
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
                else if (e.PropertyName == nameof(_viewModel.UserName) && _etUser.Text != _viewModel.UserName)
                    _etUser.Text = _viewModel.UserName;
                else if (e.PropertyName == nameof(_viewModel.Password) && _etPass.Text != _viewModel.Password)
                    _etPass.Text = _viewModel.Password;
                else if (e.PropertyName == nameof(_viewModel.BasePath))
                    _tvBasePath.Text = _viewModel.BasePath;
                else if (e.PropertyName == nameof(_viewModel.UseHttps) && _swHttps != null)
                    _swHttps.Checked = _viewModel.UseHttps;
            });
        };

        // 加载已保存配置（放在最后，此时所有 UI 绑定已完成）
        _ = _viewModel.LoadAsync();
    }

    /// <summary>点击浏览按钮——弹出目录选择对话框</summary>
    private void OnBrowseClick(object? sender, EventArgs e)
    {
        var activity = Activity;
        if (activity == null) return;

        // 验证必填字段
        if (string.IsNullOrWhiteSpace(_viewModel.Host))
        {
            _viewModel.StatusText = "请先输入主机地址";
            return;
        }

        var profile = new ConnectionProfile
        {
            Host = _viewModel.Host.Trim(),
            Port = int.TryParse(_viewModel.Port, out var p) ? p : 5005,
            UserName = _viewModel.UserName,
            Password = _viewModel.Password,
            BasePath = "/",
            UseHttps = _viewModel.UseHttps
        };

        var dialog = new WebDavBrowserDialog(activity, profile);
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
