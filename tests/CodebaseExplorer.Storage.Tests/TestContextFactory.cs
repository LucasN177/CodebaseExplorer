using System.Data.Common;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodebaseExplorer.Storage.Tests;

/// <summary>Context factory over a <see cref="TestDatabase"/>, optionally with a command that fails on purpose.</summary>
internal sealed class TestContextFactory(TestDatabase database, string? failOnCommandContaining = null) : IDbContextFactory<CodebaseExplorerDbContext>
{
    public CodebaseExplorerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CodebaseExplorerDbContext>();
        DependencyInjection.Configure(options, DependencyInjection.ConnectionString(database.FilePath));
        if (failOnCommandContaining is not null)
        {
            options.AddInterceptors(new FailingCommand(failOnCommandContaining));
        }

        return new CodebaseExplorerDbContext(options.Options);
    }

    /// <summary>Throws before executing the first command whose SQL contains the given text.</summary>
    private sealed class FailingCommand(string text) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains(text, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated failure while saving.")
                : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains(text, StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated failure while saving.")
                : ValueTask.FromResult(result);
    }
}
