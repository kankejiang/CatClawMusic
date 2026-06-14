using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 备份与恢复子页面 — 备份、恢复、管理备份文件
/// </summary>
public class BackupRestoreFragment : SettingsSubPageFragment
{
    private const int RequestManageStorage = 1001;
    private LinearLayout? _backupListContainer;
    private TextView? _tvEmptyHint;

    protected override string GetTitle() => "备份与恢复";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_backup_restore, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _backupListContainer = view.FindViewById<LinearLayout>(Resource.Id.backup_list_container);
        _tvEmptyHint = view.FindViewById<TextView>(Resource.Id.tv_empty_hint);

        var btnBackup = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_backup);
        var btnRestore = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_restore);

        if (btnBackup != null) btnBackup.Click += OnBackupClick;
        if (btnRestore != null) btnRestore.Click += OnRestoreClick;

        RefreshBackupList();
    }

    public override void OnResume()
    {
        base.OnResume();
        RefreshBackupList();
    }

    // ═══════════ 权限 ═══════════

    private bool HasStoragePermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            return Android.OS.Environment.IsExternalStorageManager;
        return Android.Content.PM.Permission.Granted ==
            AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                Context!, Android.Manifest.Permission.WriteExternalStorage);
    }

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

    private static string GetExternalStoragePath()
        => Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
           ?? "/storage/emulated/0";

    private void ShowPermissionDialog(Action onConfirm)
    {
        new GlassDialog(Context!)
            .SetTitle("需要文件访问权限")
            .AddMessage("备份和恢复功能需要访问手机存储来保存和读取备份文件。\n\n备份文件将保存在：手机存储/CatClawMusic/")
            .AddPositiveButton("去授权", (_) => onConfirm())
            .AddNegativeButton("取消")
            .Show();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        if (requestCode == RequestManageStorage)
        {
            if (grantResults.Length > 0 && grantResults[0] == Android.Content.PM.Permission.Granted)
                ShowBackupOptionsDialog();
            else
                Toast.MakeText(Context, "需要文件访问权限才能备份", ToastLength.Long)?.Show();
        }
    }

    public override void OnActivityResult(int requestCode, int resultCode, Intent? data)
    {
        if (requestCode == RequestManageStorage)
        {
            if (HasStoragePermission())
                ShowBackupOptionsDialog();
            else
                Toast.MakeText(Context, "需要文件访问权限才能备份", ToastLength.Long)?.Show();
        }
    }

    // ═══════════ 备份 ═══════════

    private void OnBackupClick(object? sender, EventArgs e)
    {
        if (!HasStoragePermission()) { ShowPermissionDialog(() => RequestStoragePermission()); return; }
        ShowBackupOptionsDialog();
    }

    private void ShowBackupOptionsDialog()
    {
        var dp = (int)(16 * Resources!.DisplayMetrics!.Density);
        var sp14 = 14f;

        var cbPlaylists = new CheckBox(Context!) { Text = "歌单", Checked = true };
        var cbPlayHistory = new CheckBox(Context!) { Text = "播放记录", Checked = true };
        var cbFavorites = new CheckBox(Context!) { Text = "收藏", Checked = true };
        var cbArtists = new CheckBox(Context!) { Text = "艺术家元数据", Checked = true };
        var cbLlm = new CheckBox(Context!) { Text = "AI模型配置", Checked = true };

        var checkBoxes = new[] { cbPlaylists, cbPlayHistory, cbFavorites, cbArtists, cbLlm };
        foreach (var cb in checkBoxes)
        {
            cb.SetTextSize(Android.Util.ComplexUnitType.Sp, sp14);
            cb.SetTextColor(Android.Graphics.Color.White);
            cb.SetPadding(0, dp / 4, 0, dp / 4);
        }

        var container = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        container.SetPadding(dp, dp / 2, dp, 0);
        foreach (var cb in checkBoxes)
            container.AddView(cb);

        new GlassDialog(Context!)
            .SetTitle("选择备份内容")
            .AddCustomView(container)
            .AddPositiveButton("开始备份", (input) =>
            {
                var items = BackupItems.None;
                if (cbPlaylists.Checked) items |= BackupItems.Playlists;
                if (cbPlayHistory.Checked) items |= BackupItems.PlayHistory;
                if (cbFavorites.Checked) items |= BackupItems.Favorites;
                if (cbArtists.Checked) items |= BackupItems.Artists;
                if (cbLlm.Checked) items |= BackupItems.LlmConfigs;

                if (items == BackupItems.None)
                {
                    Toast.MakeText(Context, "请至少选择一项备份内容", ToastLength.Short)?.Show();
                    return;
                }
                _ = DoBackupAsync(items);
            })
            .AddNegativeButton("取消")
            .Show();
    }

    private async Task DoBackupAsync(BackupItems items = BackupItems.All)
    {
        try
        {
            var backupService = MainApplication.Services.GetRequiredService<BackupService>();
            var path = await backupService.BackupAsync(GetExternalStoragePath(), items);
            Activity?.RunOnUiThread(() =>
            {
                Toast.MakeText(Context, $"备份成功\n已保存到 {path}", ToastLength.Long)?.Show();
                RefreshBackupList();
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Backup] 备份失败: {ex.Message}");
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, $"备份失败: {ex.Message}", ToastLength.Long)?.Show());
        }
    }

    // ═══════════ 恢复 ═══════════

    private void OnRestoreClick(object? sender, EventArgs e)
    {
        if (!HasStoragePermission()) { ShowPermissionDialog(() => RequestStoragePermission()); return; }
        _ = ShowRestoreDialogAsync();
    }

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

            var dialog = new GlassDialog(Context!).SetTitle("选择备份文件恢复");

            foreach (var path in backups)
            {
                var info = await BackupService.ReadBackupInfoAsync(path);
                var fileName = System.IO.Path.GetFileName(path);
                string label;
                if (info != null)
                {
                    var date = info.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    label = $"{date}  |  {info.Playlists.Count}个歌单  {info.PlayHistory.Count}条记录  {info.Artists.Count}位歌手  {info.LlmConfigs.Count}个AI配置";
                }
                else { label = fileName; }

                var capturedPath = path;
                var capturedInfo = info;
                dialog.AddItem(label, () => ShowRestoreConfirmDialog(capturedPath, capturedInfo));
            }

            dialog.AddNegativeButton("取消").Show();
        }
        catch (System.Exception ex)
        {
            Toast.MakeText(Context, $"读取备份列表失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void ShowRestoreConfirmDialog(string backupPath, BackupData? info)
    {
        var dp = (int)(16 * Resources!.DisplayMetrics!.Density);
        var sp14 = 14f;

        var checkBoxes = new List<(CheckBox cb, BackupItems item)>();
        if (info != null)
        {
            if (info.Playlists.Count > 0)
                checkBoxes.Add((new CheckBox(Context!) { Text = $"歌单 ({info.Playlists.Count}个)", Checked = true }, BackupItems.Playlists));
            if (info.PlayHistory.Count > 0)
                checkBoxes.Add((new CheckBox(Context!) { Text = $"播放记录 ({info.PlayHistory.Count}条)", Checked = true }, BackupItems.PlayHistory));
            if (info.Favorites.Count > 0)
                checkBoxes.Add((new CheckBox(Context!) { Text = $"收藏 ({info.Favorites.Count}首)", Checked = true }, BackupItems.Favorites));
            if (info.Artists.Count > 0)
                checkBoxes.Add((new CheckBox(Context!) { Text = $"艺术家元数据 ({info.Artists.Count}位)", Checked = true }, BackupItems.Artists));
            if (info.LlmConfigs.Count > 0)
                checkBoxes.Add((new CheckBox(Context!) { Text = $"AI模型配置 ({info.LlmConfigs.Count}个)", Checked = true }, BackupItems.LlmConfigs));
        }

        foreach (var (cb, _) in checkBoxes)
        {
            cb.SetTextSize(Android.Util.ComplexUnitType.Sp, sp14);
            cb.SetTextColor(Android.Graphics.Color.White);
            cb.SetPadding(0, dp / 4, 0, dp / 4);
        }

        var container = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        container.SetPadding(dp, dp / 2, dp, 0);
        foreach (var (cb, _) in checkBoxes)
            container.AddView(cb);

        var hint = new TextView(Context!) { Text = "补充缺失数据，已有同名歌单不会覆盖" };
        hint.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        hint.SetTextColor(Android.Graphics.Color.ParseColor("#99FFFFFF"));
        hint.SetPadding(dp, dp, dp, dp / 2);
        container.AddView(hint);

        new GlassDialog(Context!)
            .SetTitle("选择恢复内容")
            .AddCustomView(container)
            .AddPositiveButton("开始恢复", async (input) =>
            {
                var items = BackupItems.None;
                foreach (var (cb, item) in checkBoxes)
                    if (cb.Checked) items |= item;

                if (items == BackupItems.None)
                {
                    Toast.MakeText(Context, "请至少选择一项恢复内容", ToastLength.Short)?.Show();
                    return;
                }

                try
                {
                    var backupService = MainApplication.Services.GetRequiredService<BackupService>();
                    await backupService.RestoreAsync(backupPath, items);
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, "恢复成功", ToastLength.Short)?.Show());
                }
                catch (System.Exception ex)
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, $"恢复失败: {ex.Message}", ToastLength.Long)?.Show());
                }
            })
            .AddNegativeButton("取消")
            .Show();
    }

    // ═══════════ 备份管理 ═══════════

    private void RefreshBackupList()
    {
        if (_backupListContainer == null) return;
        _backupListContainer.RemoveAllViews();

        var backups = BackupService.ListBackups(GetExternalStoragePath());

        if (_tvEmptyHint != null)
            _tvEmptyHint.Visibility = backups.Count == 0 ? ViewStates.Visible : ViewStates.Gone;

        if (backups.Count == 0) return;

        var dp = (int)(16 * Resources!.DisplayMetrics!.Density);

        foreach (var path in backups)
        {
            var row = CreateBackupRow(path, dp);
            _backupListContainer.AddView(row);
        }
    }

    private View CreateBackupRow(string filePath, int dp)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        // 解析文件名中的日期 backup_20250115_143022
        string dateStr = fileName;
        if (fileName.StartsWith("backup_") && fileName.Length > 22)
        {
            var datePart = fileName.Substring(7); // 20250115_143022
            if (datePart.Length >= 15)
                dateStr = $"{datePart.Substring(0, 4)}-{datePart.Substring(4, 2)}-{datePart.Substring(6, 2)} {datePart.Substring(9, 2)}:{datePart.Substring(11, 2)}:{datePart.Substring(13, 2)}";
        }

        // 获取文件大小
        string sizeStr = "";
        try
        {
            var fi = new System.IO.FileInfo(filePath);
            sizeStr = fi.Length < 1024 ? $"{fi.Length}B"
                : fi.Length < 1024 * 1024 ? $"{fi.Length / 1024:F1}KB"
                : $"{fi.Length / (1024 * 1024):F1}MB";
        }
        catch { }

        var outer = new LinearLayout(Context!) { Orientation = Orientation.Horizontal };
        outer.SetPadding(0, dp / 2, 0, dp / 2);
        outer.SetGravity(GravityFlags.CenterVertical);

        // 左侧信息
        var infoLayout = new LinearLayout(Context!) { Orientation = Orientation.Vertical };
        infoLayout.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);

        var tvDate = new TextView(Context!) { Text = dateStr };
        tvDate.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
        tvDate.SetTextColor(new Android.Graphics.Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextPrimary, Android.Graphics.Color.ParseColor("#2D2438").ToArgb())));
        infoLayout.AddView(tvDate);

        if (!string.IsNullOrEmpty(sizeStr))
        {
            var tvSize = new TextView(Context!) { Text = sizeStr };
            tvSize.SetTextSize(Android.Util.ComplexUnitType.Sp, 11f);
            tvSize.SetTextColor(new Android.Graphics.Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Android.Graphics.Color.ParseColor("#B0A8BA").ToArgb())));
            infoLayout.AddView(tvSize);
        }

        outer.AddView(infoLayout);

        // 恢复按钮
        var btnRestore = new TextView(Context!) { Text = "恢复" };
        btnRestore.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        btnRestore.SetTextColor(Android.Graphics.Color.ParseColor("#FF64B5F6"));
        btnRestore.SetPadding(dp, dp / 2, dp, dp / 2);
        btnRestore.Clickable = true;
        btnRestore.Click += async (s, e) =>
        {
            var info = await BackupService.ReadBackupInfoAsync(filePath);
            ShowRestoreConfirmDialog(filePath, info);
        };
        outer.AddView(btnRestore);

        // 删除按钮
        var btnDelete = new TextView(Context!) { Text = "删除" };
        btnDelete.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        btnDelete.SetTextColor(Android.Graphics.Color.ParseColor("#FFFF6E6E"));
        btnDelete.SetPadding(dp, dp / 2, dp / 2, dp / 2);
        btnDelete.Clickable = true;
        btnDelete.Click += (s, e) => ConfirmDeleteBackup(filePath);
        outer.AddView(btnDelete);

        return outer;
    }

    private void ConfirmDeleteBackup(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        new GlassDialog(Context!)
            .SetTitle("删除备份")
            .AddMessage($"确认删除备份文件？\n\n{fileName}\n\n此操作无法撤销。")
            .AddPositiveButton("删除", (input) =>
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                    Toast.MakeText(Context, "已删除", ToastLength.Short)?.Show();
                    RefreshBackupList();
                }
                catch (System.Exception ex)
                {
                    Toast.MakeText(Context, $"删除失败: {ex.Message}", ToastLength.Long)?.Show();
                }
            })
            .AddNegativeButton("取消")
            .Show();
    }

    // ═══════════ ClickListener ═══════════

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
