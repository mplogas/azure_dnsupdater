using Microsoft.Extensions.Logging;

namespace DNSUpdater.Tests;

/// <summary>
/// Records the text of every log entry at every level, including the message of a logged exception,
/// so tests can check what would reach the logs.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(formatter(state, exception));
        if (exception != null)
            Entries.Add(exception.Message);
    }
}
