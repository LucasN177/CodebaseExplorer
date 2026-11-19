using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodebaseExplorer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchIndexStates",
                columns: table => new
                {
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IndexedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexStates", x => x.RepositoryId);
                    table.ForeignKey(
                        name: "FK_SearchIndexStates_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Hand-written (AI-003, DATABASE_SCHEMA.md §4.18): EF cannot model FTS5 virtual tables. Ids are stored as
            // lowercase hex of the 16-byte BLOB, so the trigger can match Repositories.Id with lower(hex(old.Id)).
            // A virtual table has no foreign keys; the trigger replaces ON DELETE CASCADE.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE SearchChunks USING fts5(" +
                "RepositoryId UNINDEXED, FileId UNINDEXED, StartLine UNINDEXED, EndLine UNINDEXED, Text, Keywords, " +
                "tokenize = 'unicode61 remove_diacritics 2');");
            migrationBuilder.Sql(
                "CREATE TRIGGER TR_Repositories_DeleteSearchChunks AFTER DELETE ON Repositories " +
                "BEGIN DELETE FROM SearchChunks WHERE RepositoryId = lower(hex(old.Id)); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Repositories_DeleteSearchChunks;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS SearchChunks;");
            migrationBuilder.DropTable(
                name: "SearchIndexStates");
        }
    }
}
