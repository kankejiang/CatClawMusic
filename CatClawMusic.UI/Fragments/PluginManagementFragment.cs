using Android.OS;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 插件管理页面 — 展示所有已注册插件，支持启用/禁用切换
/// </summary>
public class PluginManagementFragment : Fragment
{
    private PluginCardAdapter? _adapter;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_plugin_management, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 返回按钮
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        // 初始化 RecyclerView
        var recyclerView = view.FindViewById<RecyclerView>(Resource.Id.rv_plugins);
        if (recyclerView != null)
        {
            _adapter = new PluginCardAdapter();
            recyclerView.SetAdapter(_adapter);
            recyclerView.SetLayoutManager(new LinearLayoutManager(Context));
        }

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        RefreshPluginList();
    }

    /// <summary>从 IPluginManager 刷新插件列表数据</summary>
    private void RefreshPluginList()
    {
        if (_adapter == null) return;

        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var plugins = pluginManager.GetAllPlugins();
        _adapter.UpdatePlugins(plugins);
    }
}
