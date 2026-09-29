using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ClankerWorld.Simulation.Tests;

internal sealed class RecordingLoggerProvider<T>(RecordingLogger<T> logger) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => logger;

    public void Dispose() { }
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages.ToArray();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        messages.Enqueue(formatter(state, exception));
}
