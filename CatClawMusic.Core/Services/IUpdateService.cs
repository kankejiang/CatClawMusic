namespace CatClawMusic.Core.Services;

/// <summary>版本更新服务接口</summary>
public interface IUpdateService
{
    /// <summary>异步检查 GitHub 是否有新版本，返回最新版本号（如无更新返回 null）</summary>
    Task<string?> CheckUpdateAsync();

    /// <summary>标记某版本的更新提示已读</summary>
    void MarkVersionNotified(string version);

    /// <summary>获取当前已忽略的版本（未忽略则返回空）</summary>
    string GetIgnoredVersion();

    /// <summary>设置有待提示的版本（设置页红点依赖此标记）</summary>
    void SetPendingVersion(string version);

    /// <summary>获取待提示的版本（设置页读此标记显示红点）</summary>
    string GetPendingVersion();
}
