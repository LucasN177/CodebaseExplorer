using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodebaseExplorer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddAiConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIConversations",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", nullable: false),
                    IsCloudProvider = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIConversations_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AIMessages",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ConversationId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CitationsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContextManifestJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIMessages_AIConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AIConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AIArtifacts",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    MessageId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ElementKind = table.Column<string>(type: "TEXT", nullable: false),
                    ElementId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ArtifactKind = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    AnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIArtifacts_AIMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "AIMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AIArtifacts_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIArtifacts_ElementId_ArtifactKind",
                table: "AIArtifacts",
                columns: new[] { "ElementId", "ArtifactKind" });

            migrationBuilder.CreateIndex(
                name: "IX_AIArtifacts_MessageId",
                table: "AIArtifacts",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AIArtifacts_RepositoryId",
                table: "AIArtifacts",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AIConversations_RepositoryId_UpdatedAt",
                table: "AIConversations",
                columns: new[] { "RepositoryId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AIMessages_ConversationId_Ordinal",
                table: "AIMessages",
                columns: new[] { "ConversationId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIArtifacts");

            migrationBuilder.DropTable(
                name: "AIMessages");

            migrationBuilder.DropTable(
                name: "AIConversations");
        }
    }
}
