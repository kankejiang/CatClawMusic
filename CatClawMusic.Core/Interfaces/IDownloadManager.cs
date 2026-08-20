namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 宿主内置下载管理器（DownloadManager）的插件可见接口。
/// <para>实现位于 CatClawMusic.Maui（类型不可被插件跨程序集引用），故插件通过
/// 该 Core 接口 + DI 访问：<c>services.GetRequiredService&lt;IDownloadManager&gt;()</c>。</para>
/// </summary>
public interface IDownloadManager
{
    /// <summary>按 URL 加入下载队列（任务出现在宿主下载中心）。fileName 为保存文件名
    /// （含扩展名，如 "晴天 - 周杰伦.flac"；缺省时由下载管理器从 URL 推断）。返回任务 ID。</summary>
    string EnqueueUrl(string url, string? fileName = null);

    /// <summary>当前下载任务数（可用于入队提示）</summary>
    int TaskCount { get; }
}
