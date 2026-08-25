using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音频文件操作服务接口（SAF / 本地文件）：标签读写、内嵌歌词/封面写入、
/// 侧车 .lrc 写入、重命名、删除。
/// <para>
/// 设计动机：宿主（Maui）持有 Android SAF 权限（读取授权目录、持久化 URI 权限），
/// 插件无文件写能力。本接口把文件写能力以服务形式暴露给插件，
/// 插件通过宿主 <see cref="IServiceProvider"/> 解析使用，实现 Lyrico 式标签编辑与批量操作。
/// </para>
/// </summary>
public interface IAudioFileService
{
    /// <summary>读取音频文件完整标签（含内嵌歌词与封面）</summary>
    /// <param name="uri">SAF content:// URI 或本地绝对路径</param>
    Task<AudioTagInfo?> ReadTagsAsync(string uri);

    /// <summary>写入音频标签（只更新非 null 字段）</summary>
    /// <param name="uri">SAF content:// URI 或本地绝对路径</param>
    /// <param name="edit">要写入的字段；null 字段保持原样</param>
    Task<bool> WriteTagsAsync(string uri, AudioTagEdit edit);

    /// <summary>写侧车歌词文件（同名 .lrc），覆盖已存在内容</summary>
    /// <param name="uri">音频文件 URI</param>
    /// <param name="lrcText">LRC 文本内容</param>
    /// <returns>写入的 .lrc 文件 URI；失败返回 null</returns>
    Task<string?> WriteSidecarLyricsAsync(string uri, string lrcText);

    /// <summary>删除侧车歌词文件（同名 .lrc，若存在）</summary>
    Task<bool> DeleteSidecarLyricsAsync(string uri);

    /// <summary>重命名文件（含扩展名）</summary>
    /// <param name="uri">文件 URI</param>
    /// <param name="newName">新文件名（含扩展名，如 "周杰伦 - 晴天.mp3"）</param>
    /// <returns>重命名后的 URI；失败返回 null</returns>
    Task<string?> RenameFileAsync(string uri, string newName);

    /// <summary>删除文件（永久删除，不可恢复）</summary>
    Task<bool> DeleteFileAsync(string uri);
}
