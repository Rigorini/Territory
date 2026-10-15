using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Territory.Tests;

public sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message);

/// <summary>Records every log entry, to assert on audit events without a real sink.</summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<CapturedLog> entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. entries];

    public IReadOnlyList<CapturedLog> ForEvent(int eventId) => [.. entries.Where(e => e.EventId == eventId)];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
        // Nothing to release: entries live in memory for the duration of the test.
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, eventId.Id, formatter(state, exception)));
    }
}
