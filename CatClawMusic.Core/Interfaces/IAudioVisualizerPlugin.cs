namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 音频可视化插件接口：插件向宿主提供一个可视化视图，宿主挂到播放页 / 迷你播放器。
/// 频谱数据由宿主平台层服务（ISpectrumProvider）提供，插件视图自行订阅并渲染。
/// </summary>
/// <para>
/// 设计动机：宿主当前只有假的 EQ 动画（正弦模拟），无真实频谱。
/// 本接口 + ISpectrumProvider 配合，让插件消费真实 FFT 频谱数据自绘可视化，
/// 实现"音频可视化插件化"。
/// </para>
/// <para>
/// 注意：返回类型为 <see cref="object"/> 而非 MAUI View，
/// 是为了避免 Core 项目依赖 Microsoft.Maui.Controls。宿主收到实例后强制转换为
/// <c>Microsoft.Maui.Controls.View</c> 即可。
/// </para>
/// </summary>
public interface IAudioVisualizerPlugin : IPlugin
{
    /// <summary>可视化名称（用于选择器，如 "频谱柱状图"）</summary>
    string VisualizerName { get; }

    /// <summary>是否启用可视化</summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 创建并返回可视化视图（MAUI View，宿主将强转挂载）。
    /// <para>
    /// 每次调用应返回新实例。宿主收到后强制转换为 <c>Microsoft.Maui.Controls.View</c>。
    /// 视图内部通过 <paramref name="services"/> 获取宿主 ISpectrumProvider 订阅频谱数据并自绘。
    /// </para>
    /// </summary>
    /// <param name="services">宿主的服务提供者，用于解析宿主服务</param>
    /// <returns>MAUI View 实例（以 object 形式返回避免 Core 依赖 Maui）</returns>
    object CreateVisualizerView(IServiceProvider services);
}
