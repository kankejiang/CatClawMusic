namespace CatClawMusic.Core.Interfaces;

/// <summary>主线程调度器</summary>
public interface IMainThreadDispatcher
{
    void Post(System.Action action);
    bool IsMainThread { get; }
}
