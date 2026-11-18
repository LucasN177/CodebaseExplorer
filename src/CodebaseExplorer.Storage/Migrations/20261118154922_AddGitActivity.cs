using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodebaseExplorer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddGitActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitSnapshots",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    HeadCommitSha = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: true),
                    IsDetachedHead = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasUncommittedChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemotesJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    HistoryWindowStart = table.Column<string>(type: "TEXT", nullable: true),
                    CapturedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitSnapshots_AnalysisRuns_AnalysisRunId",
                        column: x => x.AnalysisRunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GitSnapshots_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitChanges",
                columns: table => new
                {
                    GitSnapshotId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    FileId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DistinctAuthorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LinesAdded = table.Column<long>(type: "INTEGER", nullable: true),
                    LinesDeleted = table.Column<long>(type: "INTEGER", nullable: true),
                    FirstCommitAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastCommitAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastCommitSha = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitChanges", x => new { x.GitSnapshotId, x.FileId });
                    table.ForeignKey(
                        name: "FK_GitChanges_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GitChanges_GitSnapshots_GitSnapshotId",
                        column: x => x.GitSnapshotId,
                        principalTable: "GitSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitChanges_FileId",
                table: "GitChanges",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_GitSnapshots_RepositoryId_CapturedAt",
                table: "GitSnapshots",
                columns: new[] { "RepositoryId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_GitSnapshots_AnalysisRunId",
                table: "GitSnapshots",
                column: "AnalysisRunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitChanges");

            migrationBuilder.DropTable(
                name: "GitSnapshots");
        }
    }
}
