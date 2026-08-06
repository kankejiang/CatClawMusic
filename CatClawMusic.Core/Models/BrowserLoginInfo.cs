namespace CatClawMusic.Core.Models;

/// <summary>
/// 浏览器登录配置：由音源插件提供，宿主据此打开 WebView 让用户在真实网页中登录，
/// 登录成功后从 WebView 提取 Cookie 回传给插件。
/// <para>
/// 这种方式的优势：
/// <list type="bullet">
///   <item>无需自己实现 weapi 加密 / 二维码生成 / 轮询</item>
///   <item>支持平台所有登录方式（手机号 / 验证码 / 扫码 / 第三方）</item>
///   <item>真实浏览器环境，风控触发概率低</item>
///   <item>客户端只提供通用 WebView，与具体音源解耦</item>
/// </list>
/// </para>
/// </summary>
public class BrowserLoginInfo
{
    /// <summary>登录页 URL（WebView 初始加载地址）</summary>
    public string LoginUrl { get; set; } = string.Empty;

    /// <summary>要提取 Cookie 的域名（如 music.163.com）</summary>
    public string CookieDomain { get; set; } = string.Empty;

    /// <summary>
    /// 登录成功的标识 Cookie 名称列表。
    /// 当这些 Cookie 全部存在时，认为登录成功，提取完整 Cookie 回传插件。
    /// （如网易云的 MUSIC_U）
    /// </summary>
    public List<string> SuccessCookieNames { get; set; } = new();

    /// <summary>
    /// 登录成功后的 URL 匹配模式（可选）。
    /// 当 WebView 导航到匹配此模式的 URL 时，也认为登录成功。
    /// 与 <see cref="SuccessCookieNames"/> 满足任一即可。
    /// </summary>
    public string? SuccessUrlPattern { get; set; }

    /// <summary>自定义 User-Agent（可选；某些平台需要桌面 UA 才显示完整登录页）</summary>
    public string? UserAgent { get; set; }

    /// <summary>登录页标题（显示在 WebView 页面顶部）</summary>
    public string Title { get; set; } = "登录";
}
