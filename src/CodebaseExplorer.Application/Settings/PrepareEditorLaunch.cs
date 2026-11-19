using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Source;

namespace CodebaseExplorer.Application.Settings;

/// <summary>
/// Use case: the editor command for a file of the analyzed repository (UI-009, MVP-10 "open in external editor"). Only
/// files of the current model are allowed, resolved inside the repository root by the containment-checking reader
/// (SECURITY.md §4); the path is substituted into the user's template as one argument. Starting the program is the
/// Desktop's job.
/// </summary>
public sealed class PrepareEditorLaunch(
    IUserSettingsStore settings,
    IArchitectureModelStore store,
    IRepositoryRootValidator rootValidator,
    IRepositoryFileReader fileReader)
{
    /// <summary>The stored template, or null if none is configured.</summary>
    public async Task<string?> GetTemplateAsync(CancellationToken cancellationToken) =>
        await settings.GetAsync(UserSettingKeys.EditorCommand, cancellationToken) is { Length: > 0 } template ? template : null;

    /// <summary>Validates and stores the template; an empty text removes it.</summary>
    public async Task<Result> SaveTemplateAsync(string? template, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            await settings.SetAsync(UserSettingKeys.EditorCommand, string.Empty, cancellationToken);
            return Result.Success();
        }

        if (!EditorCommandTemplate.Parse(template).TryGetValue(out var parsed, out var error))
        {
            return error;
        }

        await settings.SetAsync(UserSettingKeys.EditorCommand, parsed.Text, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<EditorLaunch>> ExecuteAsync(string repositoryId, string path, int line, CancellationToken cancellationToken)
    {
        if (await GetTemplateAsync(cancellationToken) is not { } text)
        {
            return ApplicationError.Validation(EditorErrorCodes.NotConfigured, "No external editor is configured. Enter its command in Settings.");
        }

        if (!EditorCommandTemplate.Parse(text).TryGetValue(out var template, out var templateError))
        {
            return templateError;
        }

        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var file = stored.Model.Files.FirstOrDefault(f => string.Equals(f.Path.Value, path, StringComparison.Ordinal));
        if (file is null)
        {
            return ApplicationError.NotFound(SourceErrorCodes.FileNotInModel, "The file is not part of the analyzed repository.");
        }

        if (!rootValidator.Validate(stored.Repository.Location.LocalPath).TryGetValue(out var root, out var rootError))
        {
            return ApplicationError.NotFound(SourceErrorCodes.FolderMissing, $"The repository folder is not available any more ({rootError.Message}).");
        }

        if (!fileReader.GetStatus(root, file.Path).TryGetValue(out _, out var statusError))
        {
            return ApplicationError.NotFound(SourceErrorCodes.FileMissing, $"{file.Path} cannot be opened: {statusError.Message}");
        }

        var absolute = Path.Combine(root.FullPath, file.Path.Value.Replace('/', Path.DirectorySeparatorChar));
        return template.Build(absolute, line);
    }
}
