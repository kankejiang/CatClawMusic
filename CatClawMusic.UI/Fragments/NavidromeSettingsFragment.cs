using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class NavidromeSettingsFragment : SettingsSubPageFragment
{
    private NavidromeSettingsViewModel _viewModel = null!;
    private EditText _etHost = null!, _etUsername = null!, _etPassword = null!;
    private Button _btnTest = null!;

    protected override string GetTitle() => "Navidrome 设置";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_navidrome_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<NavidromeSettingsViewModel>();

        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etUsername = view.FindViewById<EditText>(Resource.Id.et_username)!;
        _etPassword = view.FindViewById<EditText>(Resource.Id.et_password)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;

        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etUsername.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _etPassword.TextChanged += (s, e) => _viewModel.Password = e?.Text?.ToString() ?? "";
        _btnTest.Click += (s, e) => _viewModel.TestCommand.Execute(null);
    }
}
