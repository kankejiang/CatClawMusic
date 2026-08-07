namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 可配置插件接口：插件向宿主声明"我有配置界面"，宿主在插件管理页的「配置」按钮点击后
/// 调用 <see cref="CreateConfigView"/> 获取配置视图并展示（弹窗或推送页面）。
/// </summary>
/// <para>
/// 设计动机：取代宿主硬编码特定插件的配置入口（如网易云 Cookie 写死路径方案），
/// 让所有插件以统一方式提供配置 UI，实现"宿主零硬编码、插件自治配置"。
/// </para>
/// <para>
/// 注意：返回类型为 <see cref="object"/> 而非 MAUI View，
/// 是为了避免 Core 项目依赖 Microsoft.Maui.Controls。宿主收到实例后强制转换为
/// <c>Microsoft.Maui.Controls.View</c> 即可。
/// </para>
/// </summary>
public interface IPluginConfigurable : IPlugin
{
    /// <summary>当前是否可配置（宿主据此决定「配置」按钮显隐）</summary>
    bool CanConfigure { get; }

    /// <summary>
    /// 创建并返回插件配置视图（MAUI View，宿主将强转展示）。
    /// <para>
    /// 每次调用应返回新实例。宿主收到后强制转换为 <c>Microsoft.Maui.Controls.View</c>。
    /// 通过 <paramref name="services"/> 可获取宿主服务（如 INavigationService、IDialogService 等）。
    /// </para>
    /// </summary>
    /// <param name="services">宿主的服务提供者，用于解析宿主服务</param>
    /// <returns>MAUI View 实例（以 object 形式返回避免 Core 依赖 Maui）</returns>
    object CreateConfigView(IServiceProvider services);
}
