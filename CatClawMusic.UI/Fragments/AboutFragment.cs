using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>关于页面，显示应用信息、作者、开源地址、协议和QQ群</summary>
public class AboutFragment : Fragment
{
    private const string GithubUrl = "https://github.com/lvjin123/CatClawMusic";
    private const string QqGroupKey = "mqqopensdkapi://bizAgent/qm/qr?url=http%3A%2F%2Fqm.qq.com%2Fcgi-bin%2Fqm%2Fqr%3Ffrom%3Dapp%26p%3Dandroid%26jump_from%3Dwebapi%26k%3D";
    private const string QqGroupKeyFallback = "https://qm.qq.com/q/BXbVnGfUzu";
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
        if (tvVersion != null)
        {
            try
            {
                var pInfo = Context?.PackageManager?.GetPackageInfo(Context?.PackageName ?? "", 0);
                var versionName = pInfo?.VersionName ?? "1.0.0";
                tvVersion.Text = $"版本 {versionName}";
            }
            catch
            {
                tvVersion.Text = "版本 1.0.0";
            }
        }

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
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(QqGroupKeyFallback + QqGroupNumber));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch
        {
            try
            {
                var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(QqGroupKeyFallback + QqGroupNumber));
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
            catch
            {
                Toast.MakeText(Context, "请手动搜索QQ群: " + QqGroupNumber, ToastLength.Long)?.Show();
            }
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
