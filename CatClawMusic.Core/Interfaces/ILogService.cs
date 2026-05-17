namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 日志服务接口，供插件和主程序统一输出调试日志。
/// 实现类负责将日志写入文件（如 debug.log）或 logcat 等目标。
/// </summary>
public interface ILogService
{
    /// <summary>输出信息级别日志</summary>
    /// <param name="tag">日志标签（标识来源模块）</param>
    /// <param name="message">日志消息</param>
    void Info(string tag, string message);

    /// <summary>输出警告级别日志</summary>
    /// <param name="tag">日志标签</param>
    /// <param name="message">日志消息</param>
    void Warn(string tag, string message);

    /// <summary>输出错误级别日志</summary>
    /// <param name="tag">日志标签</param>
    /// <param name="message">日志消息</param>
    void Error(string tag, string message);
}
