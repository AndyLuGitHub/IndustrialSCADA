using Serilog;

namespace IndustrialSCADA.Infrastructure.Logging;

/// <summary>
/// Configures Serilog as the application-wide logging framework.
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// Creates and configures the global Serilog logger with console and file sinks.
    /// </summary>
    public static void CreateLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/scada-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
