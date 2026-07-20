using System;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 轻量文件日志提供器：把运行日志存入内存环形缓冲（约 200KB，覆盖崩溃前最近的轨迹），
/// 不频繁写盘。崩溃发生时由 <see cref="CrashReporter"/> 通过 <see cref="DumpToCrashFile"/> 落盘，
/// 从而在「无 adb」情况下也能看到崩溃前最后几步（例如封面加载走到哪一步）。
/// 注册方式：MauiProgram 中 builder.Logging.AddProvider(new FileLoggerProvider())。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly RingBuffer _buffer = new();

    /// <summary>把内存中的日志轨迹 dump 到崩溃日志文件（崩溃钩子里调用）。</summary>
    public static void DumpToCrashFile()
    {
        try
        {
            var trace = _buffer.Dump();
            CrashReporter.AppendTrace(trace);
        }
        catch { }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _buffer);

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly RingBuffer _writer;

        public FileLogger(string category, RingBuffer writer)
        {
            _category = category;
            _writer = writer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                var msg = formatter(state, exception);
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel}] {_category}: {msg}";
                _writer.Append(line);
                if (exception != null)
                    _writer.Append(exception.ToString());
            }
            catch { }
        }
    }

    private sealed class RingBuffer
    {
        private readonly object _lock = new();
        private readonly StringBuilder _sb = new();
        private const int MaxChars = 200_000;

        public void Append(string text)
        {
            lock (_lock)
            {
                _sb.AppendLine(text);
                if (_sb.Length > MaxChars)
                    _sb.Remove(0, _sb.Length - MaxChars);
            }
        }

        public string Dump()
        {
            lock (_lock) return _sb.ToString();
        }
    }
}
