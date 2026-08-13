namespace CatClawMusic.Maui.Services;

/// <summary>
/// 冷启动协调器：聚合关键服务（数据库、插件、FFmpeg）的初始化完成信号。
/// 启动页等待 <see cref="AllReadyTask"/> 完成后才放行进入主界面，
/// 避免主界面（ViewPager2 + 5 个子页面）的构建与后台服务初始化并发竞争
/// 主线程/IO，导致启动后一段时间内操作卡顿。
/// 每个信号都由对应的初始化任务在 finally 中报告（失败也放行，防止启动页卡死）。
/// </summary>
public sealed class StartupCoordinator
{
    private readonly TaskCompletionSource _database = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _plugins = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ffmpeg = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>数据库初始化完成（含失败）信号。</summary>
    public Task DatabaseReadyTask => _database.Task;

    /// <summary>插件管理器初始化完成（含失败）信号。</summary>
    public Task PluginsReadyTask => _plugins.Task;

    /// <summary>FFmpeg 初始化完成（含失败）信号。</summary>
    public Task FFmpegReadyTask => _ffmpeg.Task;

    /// <summary>全部关键服务就绪后完成的聚合任务，启动页等待它。</summary>
    public Task AllReadyTask => Task.WhenAll(DatabaseReadyTask, PluginsReadyTask, FFmpegReadyTask);

    /// <summary>报告数据库初始化结束（成功或失败均调用）。</summary>
    public void MarkDatabaseReady() => _database.TrySetResult();

    /// <summary>报告插件初始化结束（成功或失败均调用）。</summary>
    public void MarkPluginsReady() => _plugins.TrySetResult();

    /// <summary>报告 FFmpeg 初始化结束（成功或失败均调用）。</summary>
    public void MarkFFmpegReady() => _ffmpeg.TrySetResult();
}
