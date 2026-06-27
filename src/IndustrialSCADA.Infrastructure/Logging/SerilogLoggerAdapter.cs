using System;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace IndustrialSCADA.Infrastructure.Logging;

/// <summary>
/// Bridges Serilog into Microsoft.Extensions.Logging.ILogger{T}.
/// Registered as an open generic so that ILogger{T} resolves to SerilogLoggerAdapter{T}
/// for any T throughout the application.
/// </summary>
/// <typeparam name="T">The type whose name is used as the Serilog source context.</typeparam>
public sealed class SerilogLoggerAdapter<T> : ILogger<T>
{
    private readonly Serilog.ILogger _logger;

    /// <summary>
    /// Initializes a new instance that writes to the global Serilog logger,
    /// enriched with the source context type name.
    /// </summary>
    public SerilogLoggerAdapter()
    {
        _logger = Serilog.Log.ForContext("SourceContext", typeof(T).FullName);
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) =>
        _logger.IsEnabled(MapLevel(logLevel));

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        _logger.Write(MapLevel(logLevel), exception, message);
    }

    /// <summary>
    /// Maps Microsoft.Extensions.Logging.LogLevel to Serilog.Events.LogEventLevel.
    /// </summary>
    private static LogEventLevel MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };
}
