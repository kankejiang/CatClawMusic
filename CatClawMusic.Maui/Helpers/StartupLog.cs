namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 轻量启动/诊断日志（写入 %TEMP%\catclaw_startup.log，与 NowPlayingPage.Windows.WinLog 同文件）。
/// 仅供诊断封面流等异步链路使用；任何失败静默忽略，不影响业务。
/// </summary>
public static class StartupLog
{
    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catclaw_startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
