namespace CatClawMusic.Maui.Services;

/// <summary>
/// 【临时诊断】桌面内嵌导航埋点，用于定位「网易云插件点登录后内容区变空白」。
/// <para>
/// 为什么不走 <c>Log.Debug</c>：LogService 的文件输出受设置页「诊断日志」开关控制，
/// 默认关闭时整条链路（全是 try/catch + Log.Debug）完全静默——之前的排查就是卡在这里。
/// 这里无条件直写 AppDataDirectory/navdiag.log，定位完成后**本文件与全部调用点一并删除**。
/// </para>
/// </summary>
public static class NavDiagnostics
{
    private static readonly object Gate = new();

    /// <summary>日志文件路径（读取用）：&lt;AppDataDirectory&gt;/navdiag.log</summary>
    public static string FilePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "navdiag.log");

    /// <summary>清空本次启动前的旧日志（每次冷启动重新记录，避免混入历史）</summary>
    public static void Reset()
    {
        try { lock (Gate) File.WriteAllText(FilePath, $"==== navdiag {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====\n"); }
        catch { }
    }

    public static void Write(string tag, string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] t{Environment.CurrentManagedThreadId} {tag} | {message}\n");
        }
        catch { }
        System.Diagnostics.Debug.WriteLine($"[NAVDIAG] {tag} | {message}");
    }

    /// <summary>内容区快照：转场后「新页是否可见」的关键证据（opacity/tx/parent）。</summary>
    public static void DumpContainer(string tag, string what, Layout container)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"{what}: children={container.Children.Count} w={container.Width:F0} h={container.Height:F0}");
            foreach (var child in container.Children)
            {
                if (child is VisualElement ve)
                {
                    sb.Append($"\n      - {ve.GetType().Name} opacity={ve.Opacity:F2} tx={ve.TranslationX:F0} ty={ve.TranslationY:F0}"
                              + $" visible={ve.IsVisible} w={ve.Width:F0} h={ve.Height:F0} attached={ve.Parent != null}");
                }
                else
                {
                    sb.Append($"\n      - {child.GetType().Name} (非 VisualElement)");
                }
            }
            Write(tag, sb.ToString());
        }
        catch (Exception ex) { Write(tag, $"DumpContainer 失败: {ex.Message}"); }
    }
}
