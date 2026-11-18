using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodebaseExplorer.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisDiagnostics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    AnalyzerId = table.Column<string>(type: "TEXT", nullable: true),
                    FileId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: true),
                    StartColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: true),
                    EndColumn = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisDiagnostics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisEvidence",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RelationshipId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AnalyzerId = table.Column<string>(type: "TEXT", nullable: false),
                    AnalyzerVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Rule = table.Column<string>(type: "TEXT", nullable: false),
                    FileId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: true),
                    StartColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: true),
                    EndColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    Excerpt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CommitSha = table.Column<string>(type: "TEXT", nullable: true),
                    Branch = table.Column<string>(type: "TEXT", nullable: true),
                    AnalyzerVersionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    StatisticsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastOpenedAt = table.Column<string>(type: "TEXT", nullable: true),
                    LastAnalysisRunId = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repositories_AnalysisRuns_LastAnalysisRunId",
                        column: x => x.LastAnalysisRunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CodeRelationships",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", nullable: false),
                    TargetId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    LevelRank = table.Column<int>(type: "INTEGER", nullable: false),
                    Occurrences = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginAnalyzer = table.Column<string>(type: "TEXT", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceProjectId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    TargetProjectId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    SourceModuleId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    TargetModuleId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRelationships_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalDependencies",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Ecosystem = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalDependencies_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeEntities",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ProjectId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ModuleId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ContainingEntityId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    QualifiedName = table.Column<string>(type: "TEXT", nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", nullable: false),
                    SymbolKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", nullable: false),
                    Modifiers = table.Column<int>(type: "INTEGER", nullable: false),
                    Signature = table.Column<string>(type: "TEXT", nullable: true),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MemberCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeEntities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeEntities_CodeEntities_ContainingEntityId",
                        column: x => x.ContainingEntityId,
                        principalTable: "CodeEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CodeEntities_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeEntityDeclarations",
                columns: table => new
                {
                    EntityId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: false),
                    StartColumn = table.Column<int>(type: "INTEGER", nullable: false),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: false),
                    EndColumn = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeEntityDeclarations", x => new { x.EntityId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_CodeEntityDeclarations_CodeEntities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "CodeEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Classification = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ProjectId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Ecosystem = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryLanguage = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectFileId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    RootDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SolutionMembershipsJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Files_ProjectFileId",
                        column: x => x.ProjectFileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Projects_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "BLOB", nullable: false),
                    RepositoryId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ProjectId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ParentModuleId = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    QualifiedName = table.Column<string>(type: "TEXT", nullable: false),
                    NaturalKey = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenRunId = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Modules_Modules_ParentModuleId",
                        column: x => x.ParentModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Modules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Modules_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisDiagnostics_AnalysisRunId_Severity",
                table: "AnalysisDiagnostics",
                columns: new[] { "AnalysisRunId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisEvidence_AnalysisRunId",
                table: "AnalysisEvidence",
                column: "AnalysisRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisEvidence_FileId",
                table: "AnalysisEvidence",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisEvidence_RelationshipId",
                table: "AnalysisEvidence",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisEvidence_RepositoryId",
                table: "AnalysisEvidence",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_RepositoryId_StartedAt",
                table: "AnalysisRuns",
                columns: new[] { "RepositoryId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeEntities_ContainingEntityId",
                table: "CodeEntities",
                column: "ContainingEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeEntities_ModuleId",
                table: "CodeEntities",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeEntities_ProjectId_Kind",
                table: "CodeEntities",
                columns: new[] { "ProjectId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeEntities_RepositoryId_Name",
                table: "CodeEntities",
                columns: new[] { "RepositoryId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeEntityDeclarations_FileId",
                table: "CodeEntityDeclarations",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_RepositoryId_Kind_LevelRank",
                table: "CodeRelationships",
                columns: new[] { "RepositoryId", "Kind", "LevelRank" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_SourceId",
                table: "CodeRelationships",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_SourceModuleId_TargetModuleId",
                table: "CodeRelationships",
                columns: new[] { "SourceModuleId", "TargetModuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_SourceProjectId_TargetProjectId",
                table: "CodeRelationships",
                columns: new[] { "SourceProjectId", "TargetProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRelationships_TargetId",
                table: "CodeRelationships",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "UX_ExternalDependencies_RepositoryId_Ecosystem_Name",
                table: "ExternalDependencies",
                columns: new[] { "RepositoryId", "Ecosystem", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_ProjectId",
                table: "Files",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_RepositoryId_Language",
                table: "Files",
                columns: new[] { "RepositoryId", "Language" });

            migrationBuilder.CreateIndex(
                name: "UX_Files_RepositoryId_RelativePath",
                table: "Files",
                columns: new[] { "RepositoryId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ParentModuleId",
                table: "Modules",
                column: "ParentModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ProjectId",
                table: "Modules",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_RepositoryId_QualifiedName",
                table: "Modules",
                columns: new[] { "RepositoryId", "QualifiedName" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectFileId",
                table: "Projects",
                column: "ProjectFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_RepositoryId_Ecosystem",
                table: "Projects",
                columns: new[] { "RepositoryId", "Ecosystem" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_RepositoryId_Name",
                table: "Projects",
                columns: new[] { "RepositoryId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_LastAnalysisRunId",
                table: "Repositories",
                column: "LastAnalysisRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_LastOpenedAt",
                table: "Repositories",
                column: "LastOpenedAt");

            migrationBuilder.CreateIndex(
                name: "UX_Repositories_LocalPath",
                table: "Repositories",
                column: "LocalPath",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisDiagnostics_AnalysisRuns_AnalysisRunId",
                table: "AnalysisDiagnostics",
                column: "AnalysisRunId",
                principalTable: "AnalysisRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisEvidence_AnalysisRuns_AnalysisRunId",
                table: "AnalysisEvidence",
                column: "AnalysisRunId",
                principalTable: "AnalysisRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisEvidence_CodeRelationships_RelationshipId",
                table: "AnalysisEvidence",
                column: "RelationshipId",
                principalTable: "CodeRelationships",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisEvidence_Files_FileId",
                table: "AnalysisEvidence",
                column: "FileId",
                principalTable: "Files",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisEvidence_Repositories_RepositoryId",
                table: "AnalysisEvidence",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisRuns_Repositories_RepositoryId",
                table: "AnalysisRuns",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CodeEntities_Modules_ModuleId",
                table: "CodeEntities",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CodeEntities_Projects_ProjectId",
                table: "CodeEntities",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CodeEntityDeclarations_Files_FileId",
                table: "CodeEntityDeclarations",
                column: "FileId",
                principalTable: "Files",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Files_Projects_ProjectId",
                table: "Files",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_AnalysisRuns_LastAnalysisRunId",
                table: "Repositories");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Files_ProjectFileId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "AnalysisDiagnostics");

            migrationBuilder.DropTable(
                name: "AnalysisEvidence");

            migrationBuilder.DropTable(
                name: "CodeEntityDeclarations");

            migrationBuilder.DropTable(
                name: "ExternalDependencies");

            migrationBuilder.DropTable(
                name: "CodeRelationships");

            migrationBuilder.DropTable(
                name: "CodeEntities");

            migrationBuilder.DropTable(
                name: "Modules");

            migrationBuilder.DropTable(
                name: "AnalysisRuns");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
