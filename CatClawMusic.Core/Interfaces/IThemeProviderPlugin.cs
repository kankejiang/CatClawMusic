namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 主题提供者插件接口：插件向宿主提供一套可应用的主题（调色板 + 可选背景），
/// 宿主将其合并进外观设置页的可用主题列表，用户选中后宿主应用该主题。
/// </summary>
/// <para>
/// 设计动机：宿主主题（ThemeService）目前是封闭枚举 + 硬编码色板，不可扩展。
/// 本接口让插件以纯数据方式提供主题（色板字典 + 背景源），宿主负责应用与渲染，
/// 实现"皮肤插件化"。
/// </para>
/// </summary>
public interface IThemeProviderPlugin : IPlugin
{
    /// <summary>主题唯一标识（如 "neon"），用于持久化选中状态</summary>
    string ThemeId { get; }

    /// <summary>主题显示名称（如 "霓虹之夜"）</summary>
    string ThemeName { get; }

    /// <summary>排序权重，越小越靠前</summary>
    int ThemeOrder { get; }

    /// <summary>
    /// 获取主题色板：资源键 → 色值（十六进制），如 "PrimaryColor" → "#9B7ED8"。
    /// <para>
    /// 宿主 ThemeService 应用时按资源键写入 app.Resources，配合 DynamicResource 即时生效。
    /// 插件可只覆盖部分资源键（未提供的键保留宿主默认）。
    /// 返回 null 表示插件不提供色板（可能仅提供背景）。
    /// </para>
    /// </summary>
    /// <returns>资源键 → 色值 字典，或 null</returns>
    Task<Dictionary<string, string>?> GetThemeColorsAsync();

    /// <summary>
    /// 获取主题背景（可选）。返回图片源（本地文件路径或 URI），宿主按背景图渲染。
    /// 返回 null 表示无背景图（使用宿主默认背景渲染逻辑）。
    /// </summary>
    /// <returns>图片源字符串，或 null</returns>
    Task<string?> GetThemeBackgroundAsync();
}
