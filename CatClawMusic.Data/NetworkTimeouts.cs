namespace CatClawMusic.Data;

/// <summary>网络请求超时常量（统一管理，避免散落硬编码）</summary>
public static class NetworkTimeouts
{
    /// <summary>默认 HTTP 请求超时</summary>
    public const int DefaultHttpSeconds = 15;

    /// <summary>短超时（探测/检测类请求）</summary>
    public const int ProbeSeconds = 10;

    /// <summary>长超时（下载/大文件传输）</summary>
    public const int DownloadSeconds = 30;

    /// <summary>Scraper 抓取超时</summary>
    public const int ScrapeSeconds = 10;
}
