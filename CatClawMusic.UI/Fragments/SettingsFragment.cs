using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 设置主页面Fragment，显示主题选择、深浅色模式、本地音乐、远程服务和插件管理入口
/// </summary>
public class SettingsFragment : Fragment
{
    private TextView? _tvLocalStatus;
    private TextView? _tvRemoteStatus;
    private TextView? _tvPluginStatus;
    private Spinner? _spinnerTheme;
    private ImageButton? _btnDarkModeToggle;
    private IThemeService? _themeService;
    private bool _isThemeSpinnerInitialized = false;

    // 深浅色模式循环：Light → Dark → FollowSystem → Light
    private static readonly DarkModeSetting[] DarkModeCycle = {
        DarkModeSetting.Light,
        DarkModeSetting.Dark,
        DarkModeSetting.FollowSystem
    };

    /// <summary>
    /// 创建设置页面视图，初始化主题选择器、深浅色切换按钮和各类设置入口
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        _themeService = MainApplication.Services.GetRequiredService<IThemeService>();

        // 🎨 主题选择
        _spinnerTheme = view.FindViewById<Spinner>(Resource.Id.spinner_theme);
        if (_spinnerTheme != null)
        {
            SetupThemeSpinner();
        }

        // ☀🌙 深浅色切换按钮
        _btnDarkModeToggle = view.FindViewById<ImageButton>(Resource.Id.btn_dark_mode_toggle);
        if (_btnDarkModeToggle != null)
        {
            UpdateDarkModeIcon();
            _btnDarkModeToggle.Click += OnDarkModeToggleClick;
        }

        // 📁 本地音乐
        var btnFolders = view.FindViewById<View>(Resource.Id.btn_music_folders);
        _tvLocalStatus = view.FindViewById<TextView>(Resource.Id.tv_local_music_status);
        if (btnFolders != null)
            btnFolders.SetOnClickListener(new ClickListener(() => nav.PushFragment("LocalMusicSettings")));

        // ☁️ 远程音乐服务
        var btnRemoteMusic = view.FindViewById<View>(Resource.Id.btn_remote_music);
        _tvRemoteStatus = view.FindViewById<TextView>(Resource.Id.tv_remote_music_status);
        if (btnRemoteMusic != null)
            btnRemoteMusic.SetOnClickListener(new ClickListener(() => nav.PushFragment("RemoteMusic")));

        // 🧩 插件管理
        var btnPluginManagement = view.FindViewById<View>(Resource.Id.btn_plugin_management);
        _tvPluginStatus = view.FindViewById<TextView>(Resource.Id.tv_plugin_status);
        if (btnPluginManagement != null)
            btnPluginManagement.SetOnClickListener(new ClickListener(() => nav.PushFragment("PluginManagement")));

        // 通用设置
        var btnGeneralSettings = view.FindViewById<View>(Resource.Id.card_general_settings);
        if (btnGeneralSettings != null)
            btnGeneralSettings.SetOnClickListener(new ClickListener(() => nav.PushFragment("GeneralSettings")));

        return view;
    }

    /// <summary>
    /// 视图创建完成后异步加载状态信息
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _ = LoadStatusAsync();
    }

    /// <summary>
    /// Fragment恢复可见时更新深浅色模式图标
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        // 每次恢复时更新图标（可能从其他地方改了设置）
        UpdateDarkModeIcon();
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
    /// 深浅色模式切换按钮点击，循环切换Light/Dark/FollowSystem
    /// </summary>
    private void OnDarkModeToggleClick(object? sender, EventArgs e)
    {
        if (_themeService == null) return;

        // 循环切换：当前模式的下一个
        var current = _themeService.DarkModeSetting;
        var currentIdx = Array.IndexOf(DarkModeCycle, current);
        var nextIdx = (currentIdx + 1) % DarkModeCycle.Length;
        var nextMode = DarkModeCycle[nextIdx];

        _themeService.SetDarkModeSetting(nextMode);
        UpdateDarkModeIcon();

        // 提示当前模式
        var modeText = nextMode switch
        {
            DarkModeSetting.Light => "浅色模式",
            DarkModeSetting.Dark => "深色模式",
            DarkModeSetting.FollowSystem => "跟随系统",
            _ => ""
        };
        Toast.MakeText(Context, $"已切换为{modeText}", ToastLength.Short)?.Show();
    }

    /// <summary>
    /// 根据当前深浅色模式设置更新切换按钮图标
    /// </summary>
    private void UpdateDarkModeIcon()
    {
        if (_btnDarkModeToggle == null || _themeService == null) return;

        var setting = _themeService.DarkModeSetting;
        var iconRes = setting switch
        {
            DarkModeSetting.Light => Resource.Drawable.ic_light_mode,
            DarkModeSetting.Dark => Resource.Drawable.ic_dark_mode,
            DarkModeSetting.FollowSystem => Resource.Drawable.ic_system_mode,
            _ => Resource.Drawable.ic_system_mode
        };

        _btnDarkModeToggle.SetImageResource(iconRes);
    }

    /// <summary>
    /// 异步加载本地音乐、远程服务和插件状态信息，更新状态文本
    /// </summary>
    private async Task LoadStatusAsync()
    {
        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        await db.EnsureInitializedAsync();

        // 本地音乐状态
        var folderUris = FolderPicker.GetSavedFolderUris();
        var localSongCount = await db.GetLocalSongCountAsync();
        var folderText = folderUris.Count > 0
            ? $"已添加{folderUris.Count}个文件夹 | 共{localSongCount}首歌曲"
            : "尚未添加文件夹";
        _tvLocalStatus?.Post(() => _tvLocalStatus.Text = folderText);

        // 远程音乐服务状态
        var profiles = await db.GetConnectionProfilesAsync();
        var enabled = profiles.Where(p => p.IsEnabled).ToList();
        var navi = enabled.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
        var webdav = enabled.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV);

        string remoteText;
        if (enabled.Count == 0)
        {
            remoteText = "尚未连接服务";
        }
        else
        {
            var parts = new List<string>();
            if (navi != null) parts.Add("Navidrome在线");
            if (webdav != null) parts.Add("WebDAV在线");
            remoteText = $"已连接{enabled.Count}个服务 | {string.Join("、", parts)}";
        }
        _tvRemoteStatus?.Post(() => _tvRemoteStatus.Text = remoteText);

        // 插件状态
        try
        {
            var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
            var allPlugins = pluginManager.GetAllPlugins();
            var enabledCount = allPlugins.Count(p => p.IsEnabled);
            var pluginText = enabledCount > 0
                ? $"{enabledCount} 个插件已启用"
                : "无插件启用";
            _tvPluginStatus?.Post(() => _tvPluginStatus.Text = pluginText);
        }
        catch
        {
            _tvPluginStatus?.Post(() => _tvPluginStatus.Text = "插件系统未就绪");
        }
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
