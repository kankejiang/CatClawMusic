using Android.App;
using Android.Content;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

public class AgentConfigStorage : IAgentConfigStorage
{
    private const string PrefsName = "catclaw_ai";
    private ISharedPreferences GetPrefs() =>
        Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

    public string? GetString(string key, string? defaultValue = null) =>
        GetPrefs().GetString(key, defaultValue);
    public void SetString(string key, string value) =>
        GetPrefs().Edit().PutString(key, value).Apply();
    public int GetInt(string key, int defaultValue = 0) =>
        GetPrefs().GetInt(key, defaultValue);
    public void SetInt(string key, int value) =>
        GetPrefs().Edit().PutInt(key, value).Apply();
    public float GetFloat(string key, float defaultValue = 0f) =>
        GetPrefs().GetFloat(key, defaultValue);
    public void SetFloat(string key, float value) =>
        GetPrefs().Edit().PutFloat(key, value).Apply();
    public bool GetBool(string key, bool defaultValue = false) =>
        GetPrefs().GetBoolean(key, defaultValue);
    public void SetBool(string key, bool value) =>
        GetPrefs().Edit().PutBoolean(key, value).Apply();
}
