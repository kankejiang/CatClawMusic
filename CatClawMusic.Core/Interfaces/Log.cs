namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 全局静态日志门面，供所有模块（Core/Data/Maui）无需 DI 即可输出诊断日志。
/// 在应用启动时由 <see cref="ILogService"/> 实现类调用 <see cref="SetProvider"/> 注册。
/// 未注册时所有调用为 no-op（零开销），不会影响 Release 性能。
/// </summary>
public static class Log
{
    private static ILogService? _provider;

    /// <summary>注册日志服务提供者（应用启动时调用一次）</summary>
    public static void SetProvider(ILogService? provider) => _provider = provider;

    /// <summary>诊断日志是否已开启</summary>
    public static bool IsEnabled => _provider?.IsEnabled ?? false;

    public static void Debug(string tag, string message)
    {
        var p = _provider;
        if (p == null || !p.IsEnabled) return;
        p.Debug(tag, message);
    }

    public static void Info(string tag, string message)
    {
        var p = _provider;
        if (p == null || !p.IsEnabled) return;
        p.Info(tag, message);
    }

    public static void Warn(string tag, string message)
    {
        var p = _provider;
        if (p == null || !p.IsEnabled) return;
        p.Warn(tag, message);
    }

    public static void Error(string tag, string message)
    {
        var p = _provider;
        if (p == null || !p.IsEnabled) return;
        p.Error(tag, message);
    }
}
