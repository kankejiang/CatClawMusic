using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace CatClawMusic.UI.Fragments;

/// <summary>关于页面，显示应用信息、作者、开源地址、协议和QQ群</summary>
public class AboutFragment : Fragment
{
    private const string GithubUrl = "https://github.com/kankejiang/CatClawMusic";
    private const string QqGroupKey = "mqqopensdkapi://bizAgent/qm/qr?url=http%3A%2F%2Fqm.qq.com%2Fcgi-bin%2Fqm%2Fqr%3Ffrom%3Dapp%26p%3Dandroid%26jump_from%3Dwebapi%26k%3D";
    private const string QqGroupKeyFallback = "https://qm.qq.com/q/P9GGhYEz6w";
    private const string QqGroupNumber = "855383639";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_about, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 返回按钮
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        btnBack.Click += (s, e) => nav.GoBack();

        // 版本号
        var tvVersion = view.FindViewById<TextView>(Resource.Id.tv_version);
        string currentVersion = "1.0.0";
        if (tvVersion != null)
        {
            try
            {
                var pInfo = Context?.PackageManager?.GetPackageInfo(Context?.PackageName ?? "", 0);
                currentVersion = pInfo?.VersionName?.TrimStart('v') ?? "1.0.0";
                tvVersion.Text = $"版本 {currentVersion}";
            }
            catch
            {
                tvVersion.Text = "版本 1.0.0";
            }
        }

        // 检查更新：显示提示 + 红点清除
        _ = CheckAndShowUpdateAsync(view, currentVersion);

        // 开源地址 - 点击跳转浏览器
        var cardGithub = view.FindViewById<View>(Resource.Id.card_github);
        if (cardGithub != null)
            cardGithub.Click += (s, e) => OpenUrl(GithubUrl);

        // 开源协议 - 点击查看
        var cardLicense = view.FindViewById<View>(Resource.Id.card_license);
        if (cardLicense != null)
            cardLicense.Click += (s, e) => ShowLicenseDialog();

        // QQ群 - 点击跳转
        var cardQqGroup = view.FindViewById<View>(Resource.Id.card_qq_group);
        if (cardQqGroup != null)
            cardQqGroup.Click += (s, e) => OpenQqGroup();
    }

    private async Task CheckAndShowUpdateAsync(View view, string currentVersion)
    {
        try
        {
            var updateService = MainApplication.Services.GetService<IUpdateService>();
            if (updateService == null) return;

            var latestVersion = await updateService.CheckUpdateAsync();
            if (latestVersion == null) return;

            // 显示更新提示
            var promptArea = view.FindViewById<LinearLayout>(Resource.Id.update_prompt_area);
            var tvLatest = view.FindViewById<TextView>(Resource.Id.tv_latest_version);
            var btnGoUpdate = view.FindViewById<Button>(Resource.Id.btn_go_update);

            if (promptArea != null)
                promptArea.Visibility = ViewStates.Visible;
            if (tvLatest != null)
                tvLatest.Text = $"v{latestVersion}";

            if (btnGoUpdate != null)
                btnGoUpdate.Click += (s, e) => OpenUrl(GithubUrl);

            // 清除待提示标记（设置页红点消失）
            updateService.SetPendingVersion("");
            // 标记该版本已读
            updateService.MarkVersionNotified(latestVersion);
        }
        catch { }
    }

    private void OpenUrl(string url)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch
        {
            Toast.MakeText(Context, "无法打开链接", ToastLength.Short)?.Show();
        }
    }

    private void OpenQqGroup()
    {
        try
        {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(QqGroupKeyFallback));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch
        {
            Toast.MakeText(Context, "请手动搜索QQ群: " + QqGroupNumber, ToastLength.Long)?.Show();
        }
    }

    private void ShowLicenseDialog()
    {
        var ctx = Context;
        if (ctx == null) return;

        var licenseText = "MIT License\n\n" +
            "Copyright (c) 2024-2026 kankejiang\n\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy " +
            "of this software and associated documentation files (the \"Software\"), to deal " +
            "in the Software without restriction, including without limitation the rights " +
            "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell " +
            "copies of the Software, and to permit persons to whom the Software is " +
            "furnished to do so, subject to the following conditions:\n\n" +
            "The above copyright notice and this permission notice shall be included in all " +
            "copies or substantial portions of the Software.\n\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR " +
            "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, " +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE " +
            "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER " +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, " +
            "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE " +
            "SOFTWARE.";

        new Helpers.GlassDialog(ctx)
            .SetTitle("MIT License")
            .AddMessage(licenseText)
            .AddNegativeButton("确定")
            .Show();
    }
}
