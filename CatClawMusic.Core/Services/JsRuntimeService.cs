using Jint;

namespace CatClawMusic.Core.Services;

/// <summary>
/// <see cref="Interfaces.IJsRuntimeService"/> 默认实现：宿主统一持有 Jint/Acornima
/// （普通 NuGet 依赖，随宿主进 APK/应用目录，由默认 ALC 原生解析）。
/// 实现细节：EnsureLoaded 与 Jint 类型引用分属两个方法体——宿主缺失 JS 运行时时，
/// JIT 编译 TouchAssemblies 抛出的 FileNotFoundException 会被 EnsureLoaded 捕获并
/// 转成明确的升级提示，而不是让调用方收到晦涩的程序集解析异常。
/// </summary>
public sealed class JsRuntimeService : Interfaces.IJsRuntimeService
{
    private int _ensured;

    /// <inheritdoc/>
    public void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref _ensured, 1) == 1)
            return;
        try
        {
            TouchAssemblies();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _ensured, 0);
            throw new InvalidOperationException(
                "宿主 JS 运行时（Jint）不可用——当前 App 版本过旧，请升级猫爪音乐后重装插件", ex);
        }
    }

    /// <summary>触碰 Jint/Acornima 类型强制程序集解析（本方法体是唯一引用 JS 类型的地方）</summary>
    private static void TouchAssemblies()
    {
        _ = typeof(Jint.Engine).Assembly;
        _ = typeof(Acornima.Parser).Assembly;
    }

    /// <inheritdoc/>
    public Jint.Engine CreateEngine(TimeSpan? timeout = null)
    {
        return new Jint.Engine(options => options
            .LimitRecursion(5000)
            .TimeoutInterval(timeout ?? TimeSpan.FromSeconds(10)));
    }
}
