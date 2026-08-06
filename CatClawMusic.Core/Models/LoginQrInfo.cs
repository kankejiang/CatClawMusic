namespace CatClawMusic.Core.Models;

/// <summary>
/// 音源插件二维码登录信息：宿主拿到后生成二维码图片展示，等待用户扫码。
/// </summary>
public class LoginQrInfo
{
    /// <summary>二维码会话 Key（轮询登录状态时回传）</summary>
    public string QrKey { get; set; } = string.Empty;

    /// <summary>二维码内容（URL 文本，宿主本地编码为二维码图片）</summary>
    public string QrContent { get; set; } = string.Empty;

    /// <summary>插件是否支持登录（false 时宿主提示"该音源暂不支持登录"）</summary>
    public bool Supported { get; set; } = true;
}

/// <summary>
/// 二维码登录轮询结果。
/// </summary>
public class LoginCheckResult
{
    /// <summary>是否已登录成功</summary>
    public bool Success { get; set; }

    /// <summary>平台状态码（通用约定：801=等待扫码，802=已扫码待确认，800=二维码已过期）</summary>
    public int Code { get; set; }

    /// <summary>状态描述文本</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>登录成功后的账号昵称</summary>
    public string? Nickname { get; set; }
}
