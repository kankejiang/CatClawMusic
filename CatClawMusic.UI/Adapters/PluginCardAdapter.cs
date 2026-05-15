using Android.App;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Adapters;

public class PluginCardAdapter : RecyclerView.Adapter
{
    private List<PluginInfo> _plugins = new();
    private readonly HashSet<int> _expandedPositions = new();
    public event EventHandler<string>? UninstallClicked;

    public void UpdatePlugins(List<PluginInfo> plugins)
    {
        _plugins = plugins ?? new List<PluginInfo>();
        _expandedPositions.Clear();
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

        vh.SourceTag.Text = plugin.Source switch
        {
            PluginSource.BuiltIn => "内置",
            PluginSource.Installed => "已安装",
            _ => ""
        };
        vh.SourceTag.Visibility = ViewStates.Visible;

        vh.EnabledSwitch.SetOnCheckedChangeListener(null);
        vh.EnabledSwitch.Checked = plugin.IsEnabled;

        var pluginTypeId = plugin.PluginTypeId;
        vh.EnabledSwitch.SetOnCheckedChangeListener(new SwitchListener(pluginTypeId));

        vh.BtnUninstall.Visibility = plugin.CanUninstall ? ViewStates.Visible : ViewStates.Gone;
        var typeId = pluginTypeId;
        vh.BtnUninstall.Click += (s, e) => OnUninstallClick(typeId);

        var expanded = _expandedPositions.Contains(position);
        vh.LayoutCapabilities.Visibility = expanded ? ViewStates.Visible : ViewStates.Gone;
        if (expanded)
        {
            var caps = plugin.Capabilities;
            vh.TvCapabilities.Text = caps.Count > 0
                ? "📋 可用功能:\n" + string.Join("\n", caps.Select(c => $"  • {c}"))
                : "";
        }

        var pos = position;
        vh.ItemView.Click += (s, e) =>
        {
            if (_expandedPositions.Contains(pos))
                _expandedPositions.Remove(pos);
            else
                _expandedPositions.Add(pos);
            NotifyItemChanged(pos);
        };
    }

    private async void OnUninstallClick(string pluginTypeId)
    {
        var ctx = global::Android.App.Application.Context;
        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var plugin = pluginManager.GetAllPlugins().FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        new AlertDialog.Builder(MainActivity.Instance ?? ctx)
            .SetTitle("卸载插件")
            .SetMessage($"确定要卸载「{plugin.DisplayName}」吗？")
            .SetPositiveButton("卸载", async (s, e) =>
            {
                var success = await pluginManager.UninstallPluginAsync(pluginTypeId);
                var msg = success ? "已卸载" : "卸载失败";
                Toast.MakeText(ctx, msg, ToastLength.Short)?.Show();
                UninstallClicked?.Invoke(this, pluginTypeId);
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    private class PluginCardViewHolder : RecyclerView.ViewHolder
    {
        public TextView Icon { get; }
        public TextView Name { get; }
        public TextView Desc { get; }
        public TextView Version { get; }
        public TextView SourceTag { get; }
        public Switch EnabledSwitch { get; }
        public ImageButton BtnUninstall { get; }
        public LinearLayout LayoutCapabilities { get; }
        public TextView TvCapabilities { get; }

        public PluginCardViewHolder(View itemView) : base(itemView)
        {
            Icon = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_icon)!;
            Name = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_name)!;
            Desc = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_desc)!;
            Version = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_version)!;
            SourceTag = itemView.FindViewById<TextView>(Resource.Id.tv_plugin_source)!;
            EnabledSwitch = itemView.FindViewById<Switch>(Resource.Id.switch_plugin_enabled)!;
            BtnUninstall = itemView.FindViewById<ImageButton>(Resource.Id.btn_uninstall)!;
            LayoutCapabilities = itemView.FindViewById<LinearLayout>(Resource.Id.layout_capabilities)!;
            TvCapabilities = itemView.FindViewById<TextView>(Resource.Id.tv_capabilities)!;
        }
    }

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
