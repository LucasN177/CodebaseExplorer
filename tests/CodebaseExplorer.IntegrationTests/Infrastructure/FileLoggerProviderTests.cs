using CodebaseExplorer.Infrastructure.Logging;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.IntegrationTests.Infrastructure;

/// <summary>CORE-007: the rolling file logger.</summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SettableClock _clock = new(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));

    private string LogsDirectory => Path.Combine(_temp.FullPath, "logs");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Entry_IsWrittenWithTimestampLevelCategoryAndException()
    {
        string path;
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Tests.Category");
            logger.LogError(new InvalidOperationException("boom"), "Import failed with {Code}", "repository.not-found");
            path = provider.CurrentFilePath!;
        }

        Assert.Equal(Path.Combine(LogsDirectory, "codebase-explorer-20261118.log"), path);
        var text = File.ReadAllText(path);
        Assert.StartsWith("2026-11-18T09:30:00.000Z [ERR] Tests.Category: Import failed with repository.not-found", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BelowMinimumLevel_IsNotWritten()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Tests");

        logger.LogDebug("not written");

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.Null(provider.CurrentFilePath);
    }

    [Fact]
    public void Messages_AreRedacted()
    {
        using var provider = CreateProvider();
        provider.CreateLogger("Tests").LogWarning("Clone of {Url} failed", "https://user:token123@example.com/repo.git");

        var text = ReadAll(provider);

        Assert.DoesNotContain("token123", text, StringComparison.Ordinal);
        Assert.Contains("https://***@example.com/repo.git", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FullFile_RollsOverToTheNextIndex()
    {
        using var provider = CreateProvider(maxFileBytes: 200);
        var logger = provider.CreateLogger("Tests");

        for (var i = 0; i < 5; i++)
        {
            logger.LogInformation("Entry {Number} with some padding to fill the file quickly", i);
        }

        var names = Directory.GetFiles(LogsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Contains("codebase-explorer-20261118.log", names);
        Assert.Contains("codebase-explorer-20261118-1.log", names);
    }

    [Fact]
    public void NewDay_StartsANewFile()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Tests");

        logger.LogInformation("first");
        _clock.Now = _clock.Now.AddDays(1);
        logger.LogInformation("second");

        Assert.EndsWith("codebase-explorer-20261119.log", provider.CurrentFilePath, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetFiles(LogsDirectory).Length);
    }

    [Fact]
    public void OnlyTheNewestFiles_AreKept()
    {
        Directory.CreateDirectory(LogsDirectory);
        for (var day = 1; day <= 12; day++)
        {
            var file = Path.Combine(LogsDirectory, $"codebase-explorer-202610{day:00}.log");
            File.WriteAllText(file, "old");
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Utc));
        }

        using var provider = CreateProvider();
        provider.CreateLogger("Tests").LogInformation("today");

        var files = Directory.GetFiles(LogsDirectory).Select(Path.GetFileName).ToList();
        Assert.Equal(FileLoggerProvider.RetainedFiles, files.Count);
        Assert.Contains("codebase-explorer-20261118.log", files);
        Assert.DoesNotContain("codebase-explorer-20261003.log", files);
        Assert.Contains("codebase-explorer-20261012.log", files);
    }

    [Fact]
    public void UnwritableDirectory_DisablesFileLoggingWithoutThrowing()
    {
        // A file where the logs directory should be: creating the directory fails.
        File.WriteAllText(LogsDirectory, "not a directory");
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Tests");

        logger.LogError("first");
        logger.LogError("second");

        Assert.False(logger.IsEnabled(LogLevel.Error));
    }

    private FileLoggerProvider CreateProvider(long maxFileBytes = FileLoggerProvider.MaxFileBytes) =>
        new(LogsDirectory, _clock, LogLevel.Information, maxFileBytes);

    private static string ReadAll(FileLoggerProvider provider)
    {
        using var stream = new FileStream(provider.CurrentFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>A clock that stays at <see cref="Now"/> until a test moves it.</summary>
internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
