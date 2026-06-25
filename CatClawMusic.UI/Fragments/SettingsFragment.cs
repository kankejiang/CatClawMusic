using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.UI.Services;
using CatClawMusic.Core.Services.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 设置主页面Fragment，显示主题选择、深浅色模式、本地音乐、远程服务和插件管理入口
/// </summary>
public class SettingsFragment : Fragment
{
    private TextView? _tvLocalStatus;
    private TextView? _tvRemoteStatus;
    private TextView? _tvPluginStatus;
    private TextView? _tvAiStatus;
    private TextView? _tvPermissionStatus;
    private ImageButton? _btnDarkModeToggle;
    private IThemeService? _themeService;

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

        // 🎨 外观与个性化
        var btnAppearance = view.FindViewById<View>(Resource.Id.btn_appearance_settings);
        if (btnAppearance != null)
            btnAppearance.SetOnClickListener(new ClickListener(() => nav.PushFragment("AppearanceSettings")));

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

        // 🔐 权限管理
        var btnPermission = view.FindViewById<View>(Resource.Id.btn_permission_management);
        _tvPermissionStatus = view.FindViewById<TextView>(Resource.Id.tv_permission_status);
        if (btnPermission != null)
            btnPermission.SetOnClickListener(new ClickListener(() => nav.PushFragment("PermissionManagement")));

        // 通用设置
        var btnGeneralSettings = view.FindViewById<View>(Resource.Id.card_general_settings);
        if (btnGeneralSettings != null)
            btnGeneralSettings.SetOnClickListener(new ClickListener(() => nav.PushFragment("GeneralSettings")));

        // AI 助手
        var btnAiSettings = view.FindViewById<View>(Resource.Id.btn_ai_settings);
        _tvAiStatus = view.FindViewById<TextView>(Resource.Id.tv_ai_status);
        if (btnAiSettings != null)
            btnAiSettings.SetOnClickListener(new ClickListener(() => nav.PushFragment("AiSettings")));

        // 💾 备份与恢复
        var btnBackupRestore = view.FindViewById<View>(Resource.Id.btn_backup_restore);
        if (btnBackupRestore != null)
            btnBackupRestore.SetOnClickListener(new ClickListener(() => nav.PushFragment("BackupRestore")));

        // 关于
        var btnAbout = view.FindViewById<View>(Resource.Id.btn_about);
        if (btnAbout != null)
            btnAbout.SetOnClickListener(new ClickListener(() => nav.PushFragment("About")));

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
        _ = UpdatePermissionStatusAsync();
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
    /// 更新权限管理状态文本
    /// </summary>
    private async Task UpdatePermissionStatusAsync()
    {
        try
        {
            var permService = MainApplication.Services.GetRequiredService<IPermissionService>();
            var storageOk = await permService.CheckStoragePermissionAsync();
            var overlayOk = await permService.CheckOverlayPermissionAsync();
            var manageStorageOk = await permService.CheckManageStoragePermissionAsync();

            bool notificationOk = true;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                var nm = (Android.App.NotificationManager?)Context?.GetSystemService(Context.NotificationService);
                notificationOk = nm?.AreNotificationsEnabled() ?? false;
            }

            var recordAudioOk = Context?.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
                == Android.Content.PM.Permission.Granted;

            bool mediaImagesOk = false;
            if (Context != null)
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                    mediaImagesOk = Context.CheckSelfPermission(Android.Manifest.Permission.ReadMediaImages)
                        == Android.Content.PM.Permission.Granted;
                else
                    mediaImagesOk = Context.CheckSelfPermission(Android.Manifest.Permission.ReadExternalStorage)
                        == Android.Content.PM.Permission.Granted;
            }

            int total = 6;
            int granted = (notificationOk ? 1 : 0) + (overlayOk ? 1 : 0) + (recordAudioOk ? 1 : 0)
                + (mediaImagesOk ? 1 : 0) + (storageOk ? 1 : 0) + (manageStorageOk ? 1 : 0);

            string statusText;
            Android.Graphics.Color statusColor;
            if (granted == total)
            {
                statusText = "✅ 所有权限已开启";
                statusColor = Android.Graphics.Color.ParseColor("#4CAF50");
            }
            else
            {
                statusText = $"⚠️ {granted}/{total} 项权限已开启";
                statusColor = Android.Graphics.Color.ParseColor("#FF9800");
            }

            _tvPermissionStatus?.Post(() =>
            {
                _tvPermissionStatus.Text = statusText;
                _tvPermissionStatus.SetTextColor(statusColor);
            });
        }
        catch
        {
            _tvPermissionStatus?.Post(() =>
                _tvPermissionStatus.Text = "权限状态检测中…");
        }
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
        var localFolderPaths = ScanSettings.GetLocalFolderPaths();
        var totalFolders = folderUris.Count + localFolderPaths.Count;
        var localSongCount = await db.GetLocalSongCountAsync();
        var folderText = totalFolders > 0
            ? $"已添加{totalFolders}个文件夹 | 共{localSongCount}首歌曲"
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

        // AI 助手状态
        try
        {
            var aiConfig = CatClawMusic.Core.Services.AI.AgentService.LoadConfig();
            var aiText = aiConfig.Enabled && !string.IsNullOrWhiteSpace(aiConfig.ApiKey)
                ? $"已配置 ({aiConfig.Provider})"
                : "未配置";
            _tvAiStatus?.Post(() => _tvAiStatus.Text = aiText);
        }
        catch
        {
            _tvAiStatus?.Post(() => _tvAiStatus.Text = "未配置");
        }

        // 权限状态
        _ = UpdatePermissionStatusAsync();
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
