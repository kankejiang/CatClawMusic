namespace CatClawMusic.Core.Interfaces;

/// <summary>主线程调度器</summary>
public interface IMainThreadDispatcher
{
    /// <summary>在主线程上执行操作</summary>
    void Post(System.Action action);
    /// <summary>当前是否在主线程</summary>
    bool IsMainThread { get; }
}
