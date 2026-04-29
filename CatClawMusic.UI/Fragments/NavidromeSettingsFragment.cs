using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class NavidromeSettingsFragment : Fragment
{
    private NavidromeSettingsViewModel _viewModel = null!;
    private EditText _etHost = null!, _etUser = null!;
    private Button _btnTest = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_navidrome_settings, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NavidromeSettingsViewModel>();

        _etHost = view.FindViewById<EditText>(Resource.Id.et_host)!;
        _etUser = view.FindViewById<EditText>(Resource.Id.et_user)!;
        _btnTest = view.FindViewById<Button>(Resource.Id.btn_test)!;

        _etHost.TextChanged += (s, e) => _viewModel.Host = e?.Text?.ToString() ?? "";
        _etUser.TextChanged += (s, e) => _viewModel.UserName = e?.Text?.ToString() ?? "";
        _btnTest.Click += (s, e) => _viewModel.TestCommand.Execute(null);
    }
}
