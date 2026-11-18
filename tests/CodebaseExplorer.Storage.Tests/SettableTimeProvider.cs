namespace CodebaseExplorer.Storage.Tests;

/// <summary>A clock that stays at <see cref="Now"/> until a test moves it.</summary>
internal sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
