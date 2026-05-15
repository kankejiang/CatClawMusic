using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PluginManagementFragment : Fragment
{
    private PluginCardAdapter? _adapter;

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

        var btnInstall = view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_install);
        if (btnInstall != null)
            btnInstall.Click += (s, e) => ShowInstallDialog();

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        RefreshPluginList();
    }

    private void RefreshPluginList()
    {
        if (_adapter == null) return;
        var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
        var plugins = pluginManager.GetAllPlugins();
        _adapter.UpdatePlugins(plugins);
    }

    private void ShowInstallDialog()
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
            Text = "输入插件 DLL 文件的直链下载地址（如 GitHub Releases 的 .dll 链接）"
        };
        hintText.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        hintText.TextSize = 13;
        hintText.SetPadding(0, 0, 0, 20);
        layout.AddView(hintText);

        var urlInput = new EditText(ctx)
        {
            Hint = "https://github.com/.../plugin.dll",
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri
        };
        urlInput.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
        urlInput.SetHintTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        layout.AddView(urlInput);

        new AlertDialog.Builder(ctx)
            .SetTitle("安装插件")
            .SetView(layout)
            .SetPositiveButton("安装", async (s, e) =>
            {
                var url = urlInput.Text?.Trim();
                if (string.IsNullOrEmpty(url))
                {
                    Toast.MakeText(ctx, "请输入插件下载地址", ToastLength.Short)?.Show();
                    return;
                }

                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    Toast.MakeText(ctx, "请输入有效的 HTTP/HTTPS 地址", ToastLength.Short)?.Show();
                    return;
                }

                var progressDialog = new ProgressDialog(ctx);
                progressDialog.SetTitle("安装中...");
                progressDialog.SetMessage("正在连接...");
                progressDialog.SetProgressStyle(ProgressDialogStyle.Horizontal);
                progressDialog.SetCancelable(false);
                progressDialog.Max = 100;
                progressDialog.Show();

                var pluginManager = MainApplication.Services.GetRequiredService<IPluginManager>();
                var result = await pluginManager.InstallPluginAsync(url,
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
                    Toast.MakeText(ctx, "安装失败，请检查地址是否正确", ToastLength.Long)?.Show();
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }
}
