using Jint;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 宿主统一 JS 运行时服务（Jint）：插件不再各自嵌入 Jint.dll/Acornima.dll，
/// 统一经本服务触发程序集加载并创建引擎——单一版本来源、统一执行约束，
/// 并在宿主缺少 JS 运行时（旧版 App 装新插件）时给出明确的升级提示。
/// <para>插件通过 DI 解析：<c>services.GetRequiredService&lt;IJsRuntimeService&gt;()</c>。</para>
/// </summary>
public interface IJsRuntimeService
{
    /// <summary>确保 Jint/Acornima 程序集已加载进默认 ALC（幂等）。必须在首次引用
    /// Jint 类型的代码前调用；宿主缺少 JS 运行时时抛出带升级提示的异常。</summary>
    void EnsureLoaded();

    /// <summary>创建统一约束的 Jint Engine（递归上限 5000 + 超时控制）。</summary>
    /// <param name="timeout">单次脚本执行超时（默认 10 秒）</param>
    Engine CreateEngine(TimeSpan? timeout = null);
}
