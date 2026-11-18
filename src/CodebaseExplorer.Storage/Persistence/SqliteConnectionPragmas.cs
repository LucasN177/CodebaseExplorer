using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// Applies the connection settings of DATABASE_SCHEMA.md §1 whenever EF opens a connection:
/// WAL journal mode, enforced foreign keys and a 5 s busy timeout.
/// </summary>
internal sealed class SqliteConnectionPragmas : DbConnectionInterceptor
{
    /// <summary>The pragmas, in execution order.</summary>
    internal const string Sql = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
