using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Logging;

internal sealed class ApplicationLogService : ILoggerProvider, IApplicationLogService
{
    private readonly object sync = new();
    private readonly long maximumFileBytes;
    private readonly int maximumFiles;
    private readonly string currentLogPath;
    private readonly string profileDirectory;
    private string? runMarkerPath;
    private StreamWriter? writer;
    private bool disposed;

    public ApplicationLogService(IApplicationPathProvider paths)
        : this(paths, 5L * 1024 * 1024, 7)
    {
    }

    internal ApplicationLogService(
        IApplicationPathProvider paths,
        long maximumFileBytes,
        int maximumFiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 2);
        this.maximumFileBytes = maximumFileBytes;
        this.maximumFiles = maximumFiles;
        LogsDirectory = paths.LogsDirectory;
        currentLogPath = Path.Combine(LogsDirectory, "hyperterm.log");
        profileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IsEnabled = ReadInitialEnabledSetting(paths.SettingsPath);
        if (IsEnabled)
        {
            TryStartRun();
        }
    }

    public bool IsEnabled { get; private set; }
    public bool PreviousRunCrashed { get; private set; }
    public string LogsDirectory { get; }
    public event EventHandler? LogChanged;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Configure(bool enabled)
    {
        lock (sync)
        {
            if (disposed || IsEnabled == enabled)
            {
                return;
            }

            if (!enabled)
            {
                WriteCore(LogLevel.Information, "HyperTerm", "Log capture disabled.", null);
                CompleteRunCore(writeShutdownEntry: false);
                IsEnabled = false;
                return;
            }

            IsEnabled = true;
            TryStartRun();
            WriteCore(LogLevel.Information, "HyperTerm", "Log capture enabled.", null);
        }
    }

    public async Task<string> ReadTailAsync(
        int maximumBytes = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0 || !File.Exists(currentLogPath))
        {
            return string.Empty;
        }

        await using var stream = new FileStream(
            currentLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            useAsync: true);
        long offset = Math.Max(0, stream.Length - maximumBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        if (offset > 0)
        {
            await reader.ReadLineAsync(cancellationToken);
        }

        return await reader.ReadToEndAsync(cancellationToken);
    }

    public void CompleteRun()
    {
        lock (sync)
        {
            CompleteRunCore(writeShutdownEntry: true);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            writer?.Dispose();
            writer = null;
            disposed = true;
        }
    }

    private void TryStartRun()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            DetectAbandonedRuns();
            using Process process = Process.GetCurrentProcess();
            runMarkerPath = Path.Combine(
                LogsDirectory,
                "run-" + Environment.ProcessId + "-" +
                process.StartTime.ToUniversalTime().Ticks + ".active");
            File.WriteAllText(
                runMarkerPath,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EnsureWriter();
            WriteCore(
                LogLevel.Information,
                "HyperTerm",
                "Application starting. Version=" + GetType().Assembly.GetName().Version +
                "; OS=" + Environment.OSVersion.VersionString +
                "; Runtime=" + Environment.Version + ".",
                null);
            if (PreviousRunCrashed)
            {
                WriteCore(
                    LogLevel.Warning,
                    "HyperTerm",
                    "A previous run ended unexpectedly.",
                    null);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            writer?.Dispose();
            writer = null;
            runMarkerPath = null;
        }
    }

    private void DetectAbandonedRuns()
    {
        foreach (string marker in Directory.EnumerateFiles(LogsDirectory, "run-*.active"))
        {
            if (IsLiveRunMarker(marker))
            {
                continue;
            }

            PreviousRunCrashed = true;
            TryDelete(marker);
        }
    }

    private static bool IsLiveRunMarker(string marker)
    {
        string[] parts = Path.GetFileNameWithoutExtension(marker).Split('-');
        if (parts.Length != 3 ||
            !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int processId) ||
            !long.TryParse(parts[2], CultureInfo.InvariantCulture, out long startTicks))
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited &&
                   process.StartTime.ToUniversalTime().Ticks == startTicks;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void CompleteRunCore(bool writeShutdownEntry)
    {
        if (writer is not null && IsEnabled && writeShutdownEntry)
        {
            WriteCore(
                LogLevel.Information,
                "HyperTerm",
                "Application shutting down normally.",
                null);
        }

        writer?.Dispose();
        writer = null;
        if (runMarkerPath is not null)
        {
            TryDelete(runMarkerPath);
            runMarkerPath = null;
        }
    }

    private void Write(
        LogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        bool changed = false;
        lock (sync)
        {
            if (!IsEnabled || disposed)
            {
                return;
            }

            try
            {
                WriteCore(level, category, message, exception);
                changed = true;
            }
            catch (Exception writeException) when (
                writeException is IOException or UnauthorizedAccessException or
                    ObjectDisposedException)
            {
                writer?.Dispose();
                writer = null;
            }
        }

        if (changed)
        {
            LogChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WriteCore(
        LogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        EnsureWriter();
        RotateIfNeeded();
        string timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            CultureInfo.InvariantCulture);
        writer!.Write(timestamp);
        writer.Write(" [");
        writer.Write(LevelName(level));
        writer.Write("] [");
        writer.Write(Sanitize(category));
        writer.Write("] ");
        writer.WriteLine(Sanitize(message));
        if (exception is not null)
        {
            writer.WriteLine(Sanitize(exception.ToString()));
        }

        writer.Flush();
    }

    private void EnsureWriter()
    {
        if (writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(LogsDirectory);
        var stream = new FileStream(
            currentLogPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private void RotateIfNeeded()
    {
        if (writer is null || writer.BaseStream.Length < maximumFileBytes)
        {
            return;
        }

        writer.Dispose();
        writer = null;
        string oldest = Path.Combine(
            LogsDirectory,
            "hyperterm." + (maximumFiles - 1) + ".log");
        TryDelete(oldest);
        for (int index = maximumFiles - 2; index >= 1; index--)
        {
            string source = Path.Combine(LogsDirectory, "hyperterm." + index + ".log");
            if (File.Exists(source))
            {
                File.Move(
                    source,
                    Path.Combine(LogsDirectory, "hyperterm." + (index + 1) + ".log"));
            }
        }

        if (File.Exists(currentLogPath))
        {
            File.Move(currentLogPath, Path.Combine(LogsDirectory, "hyperterm.1.log"));
        }

        EnsureWriter();
    }

    private string Sanitize(string value) =>
        string.IsNullOrEmpty(profileDirectory)
            ? value
            : value.Replace(
                profileDirectory,
                "%USERPROFILE%",
                StringComparison.OrdinalIgnoreCase);

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "NON",
    };

    private static bool ReadInitialEnabledSetting(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return true;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals(
                        "CaptureLogs",
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class FileLogger(ApplicationLogService owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            owner.IsEnabled && logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
