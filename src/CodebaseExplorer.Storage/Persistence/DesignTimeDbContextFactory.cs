using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations (DEVELOPMENT_GUIDE.md §6). Migrations are generated
/// from the model, so the database file named here is never opened by that command.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CodebaseExplorerDbContext>
{
    public CodebaseExplorerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CodebaseExplorerDbContext>();
        DependencyInjection.Configure(options, DependencyInjection.ConnectionString("design-time.db"));
        return new CodebaseExplorerDbContext(options.Options);
    }
}
