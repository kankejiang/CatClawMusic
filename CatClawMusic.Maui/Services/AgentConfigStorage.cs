using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// AI Agent 配置存储，使用 MAUI Preferences API（跨平台）。
/// 替代原 Android SharedPreferences 实现。
/// </summary>
public class AgentConfigStorage : IAgentConfigStorage
{
    /// <summary>所有配置项的统一前缀，避免与其它模块键冲突</summary>
    private const string Prefix = "ai_";

    /// <summary>读取字符串类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="defaultValue">缺省值，键不存在时返回</param>
    /// <returns>配置值或缺省值</returns>
    public string? GetString(string key, string? defaultValue = null)
        => Preferences.Default.Get(Prefix + key, defaultValue ?? "");

    /// <summary>写入字符串类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="value">配置值</param>
    public void SetString(string key, string value)
        => Preferences.Default.Set(Prefix + key, value);

    /// <summary>读取整数类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="defaultValue">缺省值，键不存在时返回</param>
    /// <returns>配置值或缺省值</returns>
    public int GetInt(string key, int defaultValue = 0)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    /// <summary>写入整数类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="value">配置值</param>
    public void SetInt(string key, int value)
        => Preferences.Default.Set(Prefix + key, value);

    /// <summary>读取单精度浮点类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="defaultValue">缺省值，键不存在时返回</param>
    /// <returns>配置值或缺省值</returns>
    public float GetFloat(string key, float defaultValue = 0f)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    /// <summary>写入单精度浮点类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="value">配置值</param>
    public void SetFloat(string key, float value)
        => Preferences.Default.Set(Prefix + key, value);

    /// <summary>读取布尔类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="defaultValue">缺省值，键不存在时返回</param>
    /// <returns>配置值或缺省值</returns>
    public bool GetBool(string key, bool defaultValue = false)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    /// <summary>写入布尔类型的配置项</summary>
    /// <param name="key">配置键名</param>
    /// <param name="value">配置值</param>
    public void SetBool(string key, bool value)
        => Preferences.Default.Set(Prefix + key, value);
}
