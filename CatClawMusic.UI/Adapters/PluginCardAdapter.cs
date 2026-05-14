using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 插件卡片 RecyclerView 适配器
/// </summary>
public class PluginCardAdapter : RecyclerView.Adapter
{
    private List<PluginInfo> _plugins = new();

    /// <summary>更新插件列表数据</summary>
    public void UpdatePlugins(List<PluginInfo> plugins)
    {
        _plugins = plugins ?? new List<PluginInfo>();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _plugins.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)?.Inflate(
            Resource.Layout.item_plugin_card, parent, false);
        return new PluginCardViewHolder(view!);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not PluginCardViewHolder vh) return;
        if (position < 0 || position >= _plugins.Count) return;

        var plugin = _plugins[position];

        vh.Icon.Text = plugin.IconEmoji;
        vh.Name.Text = plugin.DisplayName;
        vh.Desc.Text = plugin.Description;
        vh.Version.Text = $"v{plugin.Version}";

        // 防止 Switch 切换时触发监听器，先移除再设置
        vh.EnabledSwitch.SetOnCheckedChangeListener(null);
        vh.EnabledSwitch.Checked = plugin.IsEnabled;

        var pluginTypeId = plugin.PluginTypeId;
        vh.EnabledSwitch.SetOnCheckedChangeListener(new SwitchListener(pluginTypeId));
    }

    /// <summary>
    /// 插件卡片 ViewHolder
    /// </summary>
    private class PluginCardViewHolder : RecyclerView.ViewHolder
    {
        public TextView Icon { get; }
        public TextView Name { get; }
        public TextView Desc { get; }
        public TextView Version { get; }
        public Switch EnabledSwitch { get; }

        public PluginCardViewHolder(View itemView) : base(itemView)
        {
            Icon = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_icon)!;
            Name = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_name)!;
            Desc = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_desc)!;
            Version = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_version)!;
            EnabledSwitch = itemView.FindViewById<Switch>(Resource.Id.switch_plugin_enabled)!;
        }
    }

    /// <summary>
    /// Switch 切换监听器 — 调用 IPluginManager 更新状态 + Toast 提示
    /// </summary>
    private class SwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly string _pluginTypeId;

        public SwitchListener(string pluginTypeId)
        {
            _pluginTypeId = pluginTypeId;
        }

        public void OnCheckedChanged(CompoundButton? buttonView, bool isChecked)
        {
            var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
            pluginManager.SetPluginEnabled(_pluginTypeId, isChecked);

            var statusText = isChecked ? "已启用" : "已禁用";
            Toast.MakeText(buttonView?.Context, $"插件{statusText}", ToastLength.Short)?.Show();
        }
    }
}
