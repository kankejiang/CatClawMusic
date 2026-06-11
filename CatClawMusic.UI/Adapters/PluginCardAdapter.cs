using Android.App;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 插件卡片适配器，支持展开详情、启用/禁用和卸载操作
/// </summary>
public class PluginCardAdapter : RecyclerView.Adapter
{
    private List<PluginInfo> _plugins = new();
    private readonly HashSet<int> _expandedPositions = new();
    /// <summary>
    /// 卸载插件事件
    /// </summary>
    public event EventHandler<string>? UninstallClicked;

    /// <summary>
    /// 更新插件列表数据
    /// </summary>
    public void UpdatePlugins(List<PluginInfo> plugins)
    {
        _plugins = plugins ?? new List<PluginInfo>();
        _expandedPositions.Clear();
        NotifyDataSetChanged();
    }

    /// <summary>
    /// 插件总数
    /// </summary>
    public override int ItemCount => _plugins.Count;

    /// <summary>
    /// 创建插件卡片ViewHolder实例
    /// </summary>
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)?.Inflate(
            Resource.Layout.item_plugin_card, parent, false);
        return new PluginCardViewHolder(view!);
    }

    /// <summary>
    /// 绑定插件数据到ViewHolder，设置点击展开/收起逻辑
    /// </summary>
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

    /// <summary>
    /// 显示卸载确认对话框并执行卸载操作
    /// </summary>
    private async void OnUninstallClick(string pluginTypeId)
    {
        var ctx = global::Android.App.Application.Context;
        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var plugin = pluginManager.GetAllPlugins().FirstOrDefault(p => p.PluginTypeId == pluginTypeId);
        if (plugin == null) return;

        new GlassDialog(MainActivity.Instance ?? ctx)
            .SetTitle("卸载插件")
            .AddMessage($"确定要卸载「{plugin.DisplayName}」吗？")
            .AddItem("🗑  确认卸载", async () =>
            {
                var success = await pluginManager.UninstallPluginAsync(pluginTypeId);
                var msg = success ? "已卸载" : "卸载失败";
                Toast.MakeText(ctx, msg, ToastLength.Short)?.Show();
                UninstallClicked?.Invoke(this, pluginTypeId);
            })
            .AddNegativeButton("取消")
            .Show();
    }

    /// <summary>
    /// 插件卡片ViewHolder，持有所有子视图引用
    /// </summary>
    private class PluginCardViewHolder : RecyclerView.ViewHolder
    {
        /// <summary>
        /// 插件图标文本
        /// </summary>
        public TextView Icon { get; }
        /// <summary>
        /// 插件名称文本
        /// </summary>
        public TextView Name { get; }
        /// <summary>
        /// 插件描述文本
        /// </summary>
        public TextView Desc { get; }
        /// <summary>
        /// 插件版本文本
        /// </summary>
        public TextView Version { get; }
        /// <summary>
        /// 插件来源标签文本
        /// </summary>
        public TextView SourceTag { get; }
        /// <summary>
        /// 启用/禁用开关
        /// </summary>
        public Switch EnabledSwitch { get; }
        /// <summary>
        /// 卸载按钮
        /// </summary>
        public ImageButton BtnUninstall { get; }
        /// <summary>
        /// 功能列表布局容器
        /// </summary>
        public LinearLayout LayoutCapabilities { get; }
        /// <summary>
        /// 功能列表文本
        /// </summary>
        public TextView TvCapabilities { get; }

        /// <summary>
        /// 初始化ViewHolder，查找所有子视图引用
        /// </summary>
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

    /// <summary>
    /// 插件启用开关监听器，处理开关状态变化
    /// </summary>
    private class SwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly string _pluginTypeId;

        /// <summary>
        /// 使用插件类型ID初始化开关监听器
        /// </summary>
        public SwitchListener(string pluginTypeId)
        {
            _pluginTypeId = pluginTypeId;
        }

        /// <summary>
        /// 开关状态变化时更新插件启用状态
        /// </summary>
        public void OnCheckedChanged(CompoundButton? buttonView, bool isChecked)
        {
            var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
            pluginManager.SetPluginEnabled(_pluginTypeId, isChecked);

            var statusText = isChecked ? "已启用" : "已禁用";
            Toast.MakeText(buttonView?.Context, $"插件{statusText}", ToastLength.Short)?.Show();
        }
    }
}
