using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 外观与个性化设置页面，包含主题颜色选择和启动页设置入口
/// </summary>
public class AppearanceSettingsFragment : SettingsSubPageFragment
{
    private Spinner? _spinnerTheme;
    private IThemeService? _themeService;
    private bool _isThemeSpinnerInitialized = false;

    protected override string GetTitle() => "外观与个性化";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_appearance_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _themeService = MainApplication.Services.GetRequiredService<IThemeService>();
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 主题颜色选择
        _spinnerTheme = view.FindViewById<Spinner>(Resource.Id.spinner_theme);
        if (_spinnerTheme != null)
        {
            SetupThemeSpinner();
        }

        // 启动页设置入口
        var btnSplashSettings = view.FindViewById<View>(Resource.Id.btn_splash_settings);
        if (btnSplashSettings != null)
            btnSplashSettings.SetOnClickListener(new ClickListener(() => nav.PushFragment("SplashSettings")));
    }

    /// <summary>
    /// Fragment恢复可见时重新同步主题选择器
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        if (_spinnerTheme != null && _themeService != null)
        {
            _spinnerTheme.SetSelection((int)_themeService.CurrentTheme, false);
        }
    }

    /// <summary>
    /// 初始化主题选择下拉框，加载主题列表并设置当前选中项
    /// </summary>
    private void SetupThemeSpinner()
    {
        if (_themeService == null || _spinnerTheme == null) return;

        var themeNames = new List<string>
        {
            "💜 紫色主题",
            "💗 粉色主题",
            "💙 蓝色主题",
            "💚 绿色主题",
            "🧡 橙色主题"
        };

        var adapter = new ArrayAdapter<string>(
            Context!,
            Android.Resource.Layout.SimpleSpinnerItem,
            themeNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);

        _spinnerTheme.Adapter = adapter;

        var currentTheme = _themeService.CurrentTheme;
        _spinnerTheme.SetSelection((int)currentTheme, false);

        _spinnerTheme.ItemSelected += (sender, e) =>
        {
            if (!_isThemeSpinnerInitialized)
            {
                _isThemeSpinnerInitialized = true;
                return;
            }

            var selectedTheme = (AppTheme)e.Position;
            if (selectedTheme != _themeService.CurrentTheme)
            {
                _themeService.SetTheme(selectedTheme);
                MainActivity.Instance?.ApplyThemeAndRefresh();
            }
        };
    }

    /// <summary>
    /// 通用点击监听器，封装 Action 回调
    /// </summary>
    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        /// <summary>
        /// 使用指定的操作初始化点击监听器
        /// </summary>
        public ClickListener(Action action) => _action = action;
        /// <summary>
        /// 触发点击回调
        /// </summary>
        public void OnClick(View? v) => _action();
    }
}
