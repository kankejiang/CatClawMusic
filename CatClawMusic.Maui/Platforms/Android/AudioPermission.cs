using Android;
using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android 媒体音频权限封装。
/// Android 13（API 33）及以上请求 <c>READ_MEDIA_AUDIO</c>（对应系统设置中的「音乐和音频」权限组）；
/// 低版本（API 32 及以下）回退到 <c>READ_EXTERNAL_STORAGE</c>。
/// 用于「使用 Android 媒体库扫描」功能，确保仅申请音乐/音频相关的细分媒体权限。
/// </summary>
public class AudioPermission : Permissions.BasePlatformPermission
{
    /// <inheritdoc />
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new[] { (Manifest.Permission.ReadMediaAudio, true) }
            : new[] { (Manifest.Permission.ReadExternalStorage, true) };
}
