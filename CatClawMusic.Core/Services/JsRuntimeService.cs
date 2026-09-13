using CatClaw.Shared.Js;

namespace CatClawMusic.Core.Services;

/// <summary>
/// <see cref="Interfaces.IJsRuntimeService"/> 默认实现：共享基类 <see cref="JsRuntimeServiceBase"/>
/// + 猫爪音乐的约束参数（递归上限 5000 / 默认超时 10 秒）。
/// <para>
/// 接口保留在 CatClawMusic.Core.Interfaces 不下沉共享库——已发布插件（.ccp）按
/// "程序集 + 命名空间 + 类型名" 的类型标识消费该接口，移动即二进制不兼容。
/// 与猫爪影视的 JsRuntimeService 仅构造参数不同，其余逻辑统一在共享库维护。
/// </para>
/// </summary>
public sealed class JsRuntimeService : JsRuntimeServiceBase, Interfaces.IJsRuntimeService
{
    public JsRuntimeService()
        : base(
            recursionLimit: 5000,
            defaultTimeout: TimeSpan.FromSeconds(10),
            unavailableMessage: "宿主 JS 运行时（Jint）不可用——当前 App 版本过旧，请升级猫爪音乐后重装插件")
    {
    }
}
