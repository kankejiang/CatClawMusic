using Android.OS;
using Android.Views;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SettingsFragment : Fragment
{
    private SettingsViewModel _viewModel = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        var nav = MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();
        view.FindViewById<View>(Resource.Id.btn_test)?.SetOnClickListener(new ClickListener(() => nav.PushFragment("WebDavSettings")));

        return view;
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
