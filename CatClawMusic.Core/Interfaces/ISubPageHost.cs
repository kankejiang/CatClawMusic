using System.Threading.Tasks;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 宿主「子页面内嵌」能力：桌面壳层（Windows 无 Shell 的窗口直连模式）下，插件等外部模块
/// 构造好的页面应内嵌到主内容区，而不是走整窗模态推页——模态会盖住侧栏与底部播放条。
/// 宿主未注册本服务或 <see cref="CanEmbed"/> 为 false 时，调用方回退到原有推页方式（Shell / 模态）。
/// </summary>
public interface ISubPageHost
{
    /// <summary>当前是否可内嵌（桌面壳层与主内容区已就绪时为 true）</summary>
    bool CanEmbed { get; }

    /// <summary>
    /// 把已构造好的页面内嵌到主内容区。
    /// 形参用 object 以保持 Core 不依赖 MAUI：宿主实现内部强转为 ContentPage。
    /// </summary>
    Task OpenEmbeddedAsync(object page);

    /// <summary>关闭当前内嵌页面，恢复其所在 tab 的内容</summary>
    Task CloseEmbeddedAsync();
}
