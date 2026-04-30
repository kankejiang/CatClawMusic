using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 设置子页面基类 — 自动处理顶部返回按钮
/// 子类只需 override OnSubViewCreated(view) 实现内容绑定
/// </summary>
public abstract class SettingsSubPageFragment : Fragment
{
    protected const int ToolbarLayoutRes = Resource.Layout.include_settings_toolbar;

    protected INavigationService? Nav { get; private set; }

    public sealed override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        Nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 绑定返回按钮
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null) btnBack.Click += (s, e) => Nav.GoBack();

        // 设置标题
        var title = view.FindViewById<TextView>(Resource.Id.toolbar_title);
        if (title != null) title.Text = GetTitle();

        OnSubViewCreated(view, state);
    }

    protected abstract string GetTitle();
    protected abstract void OnSubViewCreated(View view, Bundle? state);
}
