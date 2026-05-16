using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 插件管理Fragment，显示已安装插件列表，支持本地安装和从GitHub安装
/// </summary>
public class PluginManagementFragment : Fragment
{
    private const int PickPluginFile = 9001;
    private PluginCardAdapter? _adapter;

    /// <summary>
    /// 创建插件管理视图，初始化返回按钮、插件列表和安装按钮
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_plugin_management, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        var recyclerView = view.FindViewById<RecyclerView>(Resource.Id.rv_plugins);
        if (recyclerView != null)
        {
            _adapter = new PluginCardAdapter();
            _adapter.UninstallClicked += (s, id) => RefreshPluginList();
            recyclerView.SetAdapter(_adapter);
            recyclerView.SetLayoutManager(new LinearLayoutManager(Context));
        }

        var btnLocal = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_install_local);
        if (btnLocal != null)
            btnLocal.Click += (s, e) => PickLocalPluginFile();

        var btnGitHub = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_install_github);
        if (btnGitHub != null)
            btnGitHub.Click += (s, e) => ShowGitHubInstallDialog();

        return view;
    }

    /// <summary>
    /// Fragment恢复时刷新插件列表
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        RefreshPluginList();
    }

    /// <summary>
    /// 刷新插件列表数据
    /// </summary>
    private void RefreshPluginList()
    {
        if (_adapter == null) return;
        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var plugins = pluginManager.GetAllPlugins();
        _adapter.UpdatePlugins(plugins);
    }

    /// <summary>
    /// 打开系统文件选择器，选择本地插件文件
    /// </summary>
    private void PickLocalPluginFile()
    {
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            StartActivityForResult(intent, PickPluginFile);
        }
        catch (Exception ex)
        {
            Toast.MakeText(Context, $"无法打开文件选择器: {ex.Message}", ToastLength.Short)?.Show();
        }
    }

    /// <summary>
    /// 处理文件选择结果，复制文件到临时目录并执行安装
    /// </summary>
    public override async void OnActivityResult(int requestCode, int resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != PickPluginFile || resultCode != (int)Result.Ok || data?.Data == null)
            return;

        var ctx = Context;
        if (ctx == null) return;

        var uri = data.Data;

        // 复制 content:// URI 到临时文件
        string tempPath;
        try
        {
            using var inputStream = ctx.ContentResolver?.OpenInputStream(uri);
            if (inputStream == null) return;

            var tempDir = System.IO.Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "plugin_temp");
            System.IO.Directory.CreateDirectory(tempDir);

            var displayName = "plugin_import.dll";
            var cursor = ctx.ContentResolver?.Query(uri, null, null, null, null);
            if (cursor != null)
            {
                cursor.MoveToFirst();
                var nameIdx = cursor.GetColumnIndex(global::Android.Provider.OpenableColumns.DisplayName);
                if (nameIdx >= 0)
                {
                    displayName = cursor.GetString(nameIdx) ?? displayName;
                }
                cursor.Close();
            }

            tempPath = System.IO.Path.Combine(tempDir, displayName);
            using var fileStream = System.IO.File.Create(tempPath);
            await inputStream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            Toast.MakeText(ctx, $"读取文件失败: {ex.Message}", ToastLength.Short)?.Show();
            return;
        }

        if (!tempPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && !tempPath.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
        {
            Toast.MakeText(ctx, "请选择 .dll 或 .ccp 格式的插件文件", ToastLength.Short)?.Show();
            return;
        }

        var progressDialog = new ProgressDialog(ctx);
        progressDialog.SetTitle("安装中...");
        progressDialog.SetMessage("正在加载插件...");
        progressDialog.SetProgressStyle(ProgressDialogStyle.Horizontal);
        progressDialog.SetCancelable(false);
        progressDialog.Max = 100;
        progressDialog.Show();

        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var result = await pluginManager.InstallFromLocalFileAsync(tempPath,
            new Progress<(string Status, int Percent)>(update =>
            {
                Activity?.RunOnUiThread(() =>
                {
                    progressDialog.SetMessage(update.Status);
                    progressDialog.Progress = update.Percent;
                });
            }));

        progressDialog.Dismiss();

        if (result != null)
        {
            Toast.MakeText(ctx, $"「{result.DisplayName}」安装成功", ToastLength.Long)?.Show();
            RefreshPluginList();
        }
        else
        {
            Toast.MakeText(ctx, "安装失败，请确保文件是有效的插件 DLL", ToastLength.Long)?.Show();
        }

        // 清理临时文件
        try { System.IO.File.Delete(tempPath); } catch { }
    }

    /// <summary>
    /// 显示从GitHub安装插件的对话框，输入仓库地址后自动下载最新Release
    /// </summary>
    private void ShowGitHubInstallDialog()
    {
        var ctx = Context;
        if (ctx == null) return;

        var layout = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical
        };
        layout.SetPadding(40, 30, 40, 10);

        var hintText = new TextView(ctx)
        {
            Text = "输入 GitHub 仓库地址，应用将自动从最新 Release 下载编译好的 .dll 或 .ccp 插件。\n\n" +
                   "格式示例: https://github.com/用户名/仓库名"
        };
        hintText.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        hintText.TextSize = 13;
        hintText.SetPadding(0, 0, 0, 20);
        layout.AddView(hintText);

        var urlInput = new EditText(ctx)
        {
            Hint = "https://github.com/user/repo",
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri
        };
        urlInput.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
        urlInput.SetHintTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        layout.AddView(urlInput);

        var noteText = new TextView(ctx)
        {
            Text = "\n⚠ 请先在 GitHub 仓库创建 Release 并上传编译好的 .dll 文件"
        };
        noteText.SetTextColor(Android.Graphics.Color.ParseColor("#E0A040"));
        noteText.TextSize = 11;
        noteText.SetPadding(0, 12, 0, 0);
        layout.AddView(noteText);

        new AlertDialog.Builder(ctx)
            .SetTitle("从 GitHub 安装")
            .SetView(layout)
            .SetPositiveButton("安装", async (s, e) =>
            {
                var url = urlInput.Text?.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    Toast.MakeText(ctx, "请输入 GitHub 仓库地址", ToastLength.Short)?.Show();
                    return;
                }

                if (!url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    Toast.MakeText(ctx, "请输入有效的 GitHub 仓库地址", ToastLength.Short)?.Show();
                    return;
                }

                var progressDialog = new ProgressDialog(ctx);
                progressDialog.SetTitle("安装中...");
                progressDialog.SetMessage("正在连接 GitHub...");
                progressDialog.SetProgressStyle(ProgressDialogStyle.Horizontal);
                progressDialog.SetCancelable(false);
                progressDialog.Max = 100;
                progressDialog.Show();

                var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
                var result = await pluginManager.InstallFromGitHubAsync(url,
                    new Progress<(string Status, int Percent)>(update =>
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            progressDialog.SetMessage(update.Status);
                            progressDialog.Progress = update.Percent;
                        });
                    }));

                progressDialog.Dismiss();

                if (result != null)
                {
                    Toast.MakeText(ctx, $"「{result.DisplayName}」安装成功", ToastLength.Long)?.Show();
                    RefreshPluginList();
                }
                else
                {
                    Toast.MakeText(ctx, "安装失败，请确保仓库有包含 .dll 的 Release", ToastLength.Long)?.Show();
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }
}
