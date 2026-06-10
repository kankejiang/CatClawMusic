using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using CatClawMusic.Core.Services.AI;
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
    private TextView? _tvAiStatus;
    private TextView? _tvBackupStatus;
    private TextView? _tvPermissionStatus;
    private Spinner? _spinnerTheme;
    private ImageButton? _btnDarkModeToggle;
    private IThemeService? _themeService;
    private bool _isThemeSpinnerInitialized = false;

    private const int RequestManageStorage = 1001;

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
        _tvBackupStatus = view.FindViewById<TextView>(Resource.Id.tv_backup_status);
        var btnBackup = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_backup);
        var btnRestore = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_restore);
        if (btnBackup != null)
            btnBackup.Click += OnBackupClick;
        if (btnRestore != null)
            btnRestore.Click += OnRestoreClick;

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
        UpdateBackupStatus();
        UpdatePermissionStatus();
    }

    /// <summary>
    /// 处理权限请求结果
    /// </summary>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        if (requestCode == RequestManageStorage)
        {
            if (grantResults.Length > 0 && grantResults[0] == Android.Content.PM.Permission.Granted)
            {
                _ = DoBackupAsync();
            }
            else
            {
                Toast.MakeText(Context, "需要文件访问权限才能备份", ToastLength.Long)?.Show();
            }
        }
    }

    /// <summary>
    /// 处理 Activity Result（MANAGE_EXTERNAL_STORAGE）
    /// </summary>
    public override void OnActivityResult(int requestCode, int resultCode, Intent? data)
    {
        if (requestCode == RequestManageStorage)
        {
            if (HasStoragePermission())
            {
                _ = DoBackupAsync();
            }
            else
            {
                Toast.MakeText(Context, "需要文件访问权限才能备份", ToastLength.Long)?.Show();
            }
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

    // ═══════════ 备份与恢复 ═══════════

    /// <summary>检查是否有文件访问权限</summary>
    private bool HasStoragePermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            return Android.OS.Environment.IsExternalStorageManager;
        }
        return Android.Content.PM.Permission.Granted ==
            AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                Context!, Android.Manifest.Permission.WriteExternalStorage);
    }

    /// <summary>请求文件访问权限</summary>
    private void RequestStoragePermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            try
            {
                var intent = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                intent.SetData(Android.Net.Uri.Parse($"package:{Context?.PackageName}"));
                StartActivityForResult(intent, RequestManageStorage);
            }
            catch
            {
                var intent = new Intent(Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                StartActivityForResult(intent, RequestManageStorage);
            }
        }
        else
        {
            AndroidX.Core.App.ActivityCompat.RequestPermissions(
                Activity!, new[] { Android.Manifest.Permission.WriteExternalStorage }, RequestManageStorage);
        }
    }

    /// <summary>获取外部存储根目录</summary>
    private static string GetExternalStoragePath()
        => Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
           ?? "/storage/emulated/0";

    /// <summary>备份按钮点击</summary>
    private void OnBackupClick(object? sender, EventArgs e)
    {
        if (!HasStoragePermission())
        {
            ShowPermissionDialog(() => RequestStoragePermission());
            return;
        }
        _ = DoBackupAsync();
    }

    /// <summary>恢复按钮点击</summary>
    private void OnRestoreClick(object? sender, EventArgs e)
    {
        if (!HasStoragePermission())
        {
            ShowPermissionDialog(() => RequestStoragePermission());
            return;
        }
        _ = ShowRestoreDialogAsync();
    }

    /// <summary>显示权限说明对话框</summary>
    private void ShowPermissionDialog(Action onConfirm)
    {
        var dialog = new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("需要文件访问权限")
            .SetMessage("备份和恢复功能需要访问手机存储来保存和读取备份文件。\n\n备份文件将保存在：手机存储/CatClawMusic/")
            .SetPositiveButton("去授权", (s, e) => onConfirm())
            .SetNegativeButton("取消", (s, e) => { })
            .Create();
        dialog.Show();
    }

    /// <summary>执行备份</summary>
    private async Task DoBackupAsync()
    {
        try
        {
            var backupService = MainApplication.Services.GetRequiredService<BackupService>();
            var path = await backupService.BackupAsync(GetExternalStoragePath());

            Activity?.RunOnUiThread(() =>
            {
                Toast.MakeText(Context, $"备份成功\n已保存到 {path}", ToastLength.Long)?.Show();
                UpdateBackupStatus();
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 备份失败: {ex.Message}");
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, $"备份失败: {ex.Message}", ToastLength.Long)?.Show());
        }
    }

    /// <summary>显示恢复选择对话框</summary>
    private async Task ShowRestoreDialogAsync()
    {
        try
        {
            var backups = BackupService.ListBackups(GetExternalStoragePath());
            if (backups.Count == 0)
            {
                Toast.MakeText(Context, "未找到备份文件\n备份目录：手机存储/CatClawMusic/", ToastLength.Long)?.Show();
                return;
            }

            // 读取备份信息用于显示
            var displayItems = new System.Collections.Generic.List<string>();
            foreach (var path in backups)
            {
                var info = await BackupService.ReadBackupInfoAsync(path);
                var fileName = System.IO.Path.GetFileName(path);
                if (info != null)
                {
                    var date = info.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    var summary = $"{date}  |  {info.Playlists.Count}个歌单  {info.PlayHistory.Count}条记录  {info.Artists.Count}位歌手  {info.LlmConfigs.Count}个AI配置";
                    displayItems.Add(summary);
                }
                else
                {
                    displayItems.Add(fileName);
                }
            }

            int selectedIndex = -1;
            var dialog = new Android.App.AlertDialog.Builder(Context!)
                .SetTitle("选择备份文件恢复")
                .SetSingleChoiceItems(displayItems.ToArray(), -1, (s, e) => selectedIndex = e.Which)
                .SetPositiveButton("恢复", (s, e) =>
                {
                    if (selectedIndex >= 0 && selectedIndex < backups.Count)
                    {
                        ShowRestoreConfirmDialog(backups[selectedIndex]);
                    }
                    else
                    {
                        Toast.MakeText(Context, "请先选择一个备份文件", ToastLength.Short)?.Show();
                    }
                })
                .SetNegativeButton("取消", (s, e) => { })
                .Create();
            dialog.Show();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 读取备份列表失败: {ex.Message}");
            Toast.MakeText(Context, $"读取备份列表失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>恢复确认对话框</summary>
    private void ShowRestoreConfirmDialog(string backupPath)
    {
        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("确认恢复")
            .SetMessage("恢复操作将：\n\n• 补充缺失的歌单（已有同名歌单不会覆盖）\n• 合并播放记录和收藏\n• 补充缺失的艺术家元数据\n• 覆盖AI模型配置\n\n确认恢复？")
            .SetPositiveButton("恢复", async (s, e) =>
            {
                try
                {
                    var backupService = MainApplication.Services.GetRequiredService<BackupService>();
                    await backupService.RestoreAsync(backupPath);
                    Activity?.RunOnUiThread(() =>
                    {
                        Toast.MakeText(Context, "恢复成功", ToastLength.Short)?.Show();
                        UpdateBackupStatus();
                    });
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Settings] 恢复失败: {ex.Message}");
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, $"恢复失败: {ex.Message}", ToastLength.Long)?.Show());
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Create()
            .Show();
    }

    /// <summary>更新备份状态文本</summary>
    private void UpdateBackupStatus()
    {
        try
        {
            var backups = BackupService.ListBackups(GetExternalStoragePath());
            if (backups.Count > 0)
            {
                var latest = System.IO.Path.GetFileNameWithoutExtension(backups[0]);
                // 从 backup_20250101_120000 提取日期
                var datePart = latest.Replace("backup_", "").Replace("_", " ");
                _tvBackupStatus?.Post(() =>
                    _tvBackupStatus.Text = $"最近备份: {datePart}  |  共 {backups.Count} 个备份");
            }
            else
            {
                _tvBackupStatus?.Post(() =>
                    _tvBackupStatus.Text = "备份歌单、播放记录、艺术家元数据、AI配置");
            }
        }
        catch
        {
            _tvBackupStatus?.Post(() =>
                _tvBackupStatus.Text = "备份歌单、播放记录、艺术家元数据、AI配置");
        }
    }

    /// <summary>
    /// 更新权限管理状态文本
    /// </summary>
    private void UpdatePermissionStatus()
    {
        try
        {
            var permService = MainApplication.Services.GetRequiredService<IPermissionService>();
            var storageOk = permService.CheckStoragePermissionAsync().Result;
            var overlayOk = permService.CheckOverlayPermissionAsync().Result;
            var manageStorageOk = permService.CheckManageStoragePermissionAsync().Result;

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

        // 备份状态
        UpdateBackupStatus();

        // 权限状态
        UpdatePermissionStatus();
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
