using Android.Content;
using Android.Views;
// 消除与 MAUI Microsoft.Maui.Controls.View 的歧义（同 FrostedBackgroundHandler 的做法）
using View = Android.Views.View;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 为 Android View 提供 GPU 硬件层的开启/关闭封装。
/// 用途：Tab 整页平移 / 切页动画期间给页面上硬件层，位移由 GPU 合成，避免主线程重绘卡顿。
/// 注意：<see cref="Android.Views.View"/> 同时存在名为 LayerType 的嵌套枚举类型与同名实例属性，
/// 成员访问（T.LayerType）会被实例属性遮蔽，无法取得枚举类型。
/// 因此在 View 子类的内部用简单名 LayerType 才能命中嵌套枚举类型，借由本类暴露枚举值。
/// </summary>
public static class HardwareLayerExtensions
{
    // 仅用于在 View 子类体内解析嵌套枚举 LayerType 的探针类型（不实例化）
    private sealed class LayerTypeProbe : View
    {
        // ReSharper disable once UnusedMember.Local
        public LayerTypeProbe(Context context) : base(context) { }

        // 在 View 子类体内，LayerType 简单名解析为嵌套枚举类型（类型名查找优先于实例属性）
        public static readonly LayerType Hardware = LayerType.Hardware;
        public static readonly LayerType None = LayerType.None;
    }

    /// <summary>开启或关闭该 View 的 GPU 硬件层。enable=true 上硬件层，false 关闭以释放显存。</summary>
    /// <param name="view">目标 Android View。</param>
    /// <param name="enabled">是否启用硬件层。</param>
    public static void SetHardwareLayer(this View view, bool enabled)
    {
        view.SetLayerType(enabled ? LayerTypeProbe.Hardware : LayerTypeProbe.None, null);
    }
}
