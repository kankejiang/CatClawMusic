using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// AI Agent 配置存储，使用 MAUI Preferences API（跨平台）。
/// 替代原 Android SharedPreferences 实现。
/// </summary>
public class AgentConfigStorage : IAgentConfigStorage
{
    private const string Prefix = "ai_";

    public string? GetString(string key, string? defaultValue = null)
        => Preferences.Default.Get(Prefix + key, defaultValue ?? "");

    public void SetString(string key, string value)
        => Preferences.Default.Set(Prefix + key, value);

    public int GetInt(string key, int defaultValue = 0)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    public void SetInt(string key, int value)
        => Preferences.Default.Set(Prefix + key, value);

    public float GetFloat(string key, float defaultValue = 0f)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    public void SetFloat(string key, float value)
        => Preferences.Default.Set(Prefix + key, value);

    public bool GetBool(string key, bool defaultValue = false)
        => Preferences.Default.Get(Prefix + key, defaultValue);

    public void SetBool(string key, bool value)
        => Preferences.Default.Set(Prefix + key, value);
}
