using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IndustrialSCADA.Module.LogViewer.ViewModels;

/// <summary>
/// ViewModel for the Log Viewer view.
/// Reads Serilog log files from disk and displays filtered entries.
/// </summary>
public partial class LogViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Represents a single log entry parsed from Serilog log files.
    /// </summary>
    public record LogEntry(DateTime Timestamp, string Level, string Message, string? Source);

    private static readonly Regex LogLineRegex = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w+)\] (.+)$", RegexOptions.Compiled);

    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _disposed;

    /// <summary>
    /// Gets the display title for the log viewer view.
    /// </summary>
    public string Title => "Log Viewer";

    /// <summary>
    /// Gets the collection of log entries currently displayed.
    /// </summary>
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    [ObservableProperty]
    private string selectedLevel = "All";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int entryCount;

    [ObservableProperty]
    private bool autoRefresh = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogViewerViewModel"/> class.
    /// </summary>
    public LogViewerViewModel()
    {
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshAsync();
        UpdateTimerState();
    }

    partial void OnAutoRefreshChanged(bool value)
    {
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        if (AutoRefresh)
        {
            _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        if (!Directory.Exists(logDirectory))
        {
            LogEntries.Clear();
            EntryCount = 0;
            return;
        }

        var entries = new List<LogEntry>();

        try
        {
            var logFiles = Directory.GetFiles(logDirectory, "scada-*.log")
                .OrderByDescending(f => f)
                .Take(5)
                .ToList();

            foreach (var logFile in logFiles)
            {
                var lines = await File.ReadAllLinesAsync(logFile);
                foreach (var line in lines)
                {
                    var match = LogLineRegex.Match(line);
                    if (match.Success)
                    {
                        var timestamp = DateTime.Parse(match.Groups[1].Value);
                        var level = match.Groups[2].Value;
                        var message = match.Groups[3].Value;

                        entries.Add(new LogEntry(timestamp, level, message, null));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Silently handle file access errors
        }

        // Apply filters
        var filtered = entries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedLevel) && SelectedLevel != "All")
        {
            filtered = filtered.Where(e => e.Level.Equals(SelectedLevel, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            filtered = filtered.Where(e => e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var result = filtered.OrderByDescending(e => e.Timestamp).ToList();

        LogEntries.Clear();
        foreach (var entry in result)
        {
            LogEntries.Add(entry);
        }

        EntryCount = LogEntries.Count;
    }

    [RelayCommand]
    private void Clear()
    {
        LogEntries.Clear();
        EntryCount = 0;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"log_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Level,Source,Message");

            foreach (var entry in LogEntries)
            {
                var message = entry.Message.Replace("\"", "\"\"");
                sb.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{entry.Level},{entry.Source ?? "Unknown"},\"{message}\"");
            }

            await File.WriteAllTextAsync(tempFile, sb.ToString());
        }
        catch (Exception)
        {
            // Silently handle export errors
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= async (_, _) => await RefreshAsync();
        GC.SuppressFinalize(this);
    }
}
