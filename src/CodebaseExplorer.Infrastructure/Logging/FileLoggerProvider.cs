using System.Globalization;
using System.Text;
using CodebaseExplorer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Infrastructure.Logging;

/// <summary>
/// Minimal in-house rolling file logger (CORE-007; chosen over a third-party sink to avoid a dependency).
/// Writes UTF-8 lines to <c>&lt;data&gt;/logs/codebase-explorer-yyyyMMdd[-n].log</c>: a new file per UTC day
/// and whenever the current file reaches the size limit; only the newest <see cref="RetainedFiles"/> files
/// are kept. Every message and exception text passes <see cref="LogRedactor"/>.
/// </summary>
/// <remarks>
/// Writes are synchronous under a lock and flushed per entry, so the entry for a crash is on disk before
/// the process ends; the logging volume is low (information level). Failing to write never throws: the
/// provider stops writing to the file and the other providers keep working.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int RetainedFiles = 10;
    public const string FilePrefix = "codebase-explorer-";

    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly LogLevel _minimumLevel;
    private readonly long _maxFileBytes;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private string? _currentDay;
    private int _currentIndex;
    private bool _failed;
    private bool _disposed;

    public FileLoggerProvider(IApplicationPaths paths, TimeProvider timeProvider)
        : this(paths.LogsDirectory, timeProvider, LogLevel.Information, MaxFileBytes)
    {
    }

    internal FileLoggerProvider(string directory, TimeProvider timeProvider, LogLevel minimumLevel, long maxFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _directory = directory;
        _timeProvider = timeProvider;
        _minimumLevel = minimumLevel;
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>The directory the log files are written to.</summary>
    public string Directory => _directory;

    /// <summary>The file currently written to, or <see langword="null"/> before the first entry.</summary>
    public string? CurrentFilePath { get; private set; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel && !_failed;

    internal void Write(LogLevel logLevel, string category, EventId eventId, string message, Exception? exception)
    {
        var now = _timeProvider.GetUtcNow();
        var entry = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(" [").Append(Abbreviation(logLevel)).Append("] ")
            .Append(category);
        if (eventId.Id != 0)
        {
            entry.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        entry.Append(": ").Append(LogRedactor.Redact(message));
        if (exception is not null)
        {
            entry.AppendLine().Append(LogRedactor.Redact(exception.ToString()));
        }

        lock (_lock)
        {
            if (_disposed || _failed)
            {
                return;
            }

            try
            {
                var writer = EnsureWriter(now);
                writer.WriteLine(entry.ToString());
                writer.Flush();
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Never let logging take the application down.
                _failed = true;
                _writer?.Dispose();
                _writer = null;
                System.Diagnostics.Debug.WriteLine($"File logging disabled: {writeError.Message}");
            }
        }
    }

    private StreamWriter EnsureWriter(DateTimeOffset now)
    {
        var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (_writer is not null && day == _currentDay && _writer.BaseStream.Length < _maxFileBytes)
        {
            return _writer;
        }

        _writer?.Dispose();
        _writer = null;
        if (day != _currentDay)
        {
            _currentDay = day;
            _currentIndex = 0;
        }

        CreateDirectory();
        var path = PathOf(day, _currentIndex);
        while (File.Exists(path) && new FileInfo(path).Length >= _maxFileBytes)
        {
            path = PathOf(day, ++_currentIndex);
        }

        // Shared, so that a second app instance and log viewers can open the file.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        CurrentFilePath = path;
        DeleteOldFiles();
        return _writer;
    }

    private string PathOf(string day, int index) => Path.Combine(_directory, index == 0
        ? $"{FilePrefix}{day}.log"
        : $"{FilePrefix}{day}-{index.ToString(CultureInfo.InvariantCulture)}.log");

    private void CreateDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(_directory);
        }
        else
        {
            // User-only, like the data directory (SECURITY.md).
            System.IO.Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private void DeleteOldFiles()
    {
        var outdated = new DirectoryInfo(_directory).GetFiles($"{FilePrefix}*.log")
            .Where(f => !string.Equals(f.FullName, CurrentFilePath, StringComparison.Ordinal))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(RetainedFiles - 1);
        foreach (var file in outdated)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // Still open elsewhere; removed on a later rollover.
            }
        }
    }

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, eventId, formatter(state, exception), exception);
            }
        }
    }
}
