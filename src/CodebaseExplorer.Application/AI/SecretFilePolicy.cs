using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Files whose content never reaches the search index or a prompt (AI_ARCHITECTURE.md §5: secret files, binaries,
/// generated code). Matching is by file name only and case-insensitive.
/// </summary>
public static class SecretFilePolicy
{
    private static readonly string[] SecretExtensions =
        [".pem", ".key", ".pfx", ".p12", ".jks", ".keystore", ".kdbx", ".crt", ".cer", ".der", ".gpg", ".asc"];

    private static readonly string[] SecretNames =
        [".npmrc", ".pypirc", ".netrc", "_netrc", ".git-credentials", "secrets.json", "credentials", "credentials.json", ".htpasswd"];

    private static readonly string[] SecretPrefixes = [".env", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519"];

    /// <summary>True if the file may be read for AI context: indexed, cited and shown as snippet.</summary>
    public static bool IsAllowed(RepositoryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Classification is FileClassification.Source or FileClassification.Test or FileClassification.ProjectManifest
                or FileClassification.Configuration or FileClassification.Documentation
            && !IsSecretName(file.Path.Value);
    }

    /// <summary>
    /// True for names that typically hold credentials: <c>.env*</c>, private keys and certificates, package-manager
    /// credential files, and environment-specific <c>appsettings.*.json</c> (which commonly hold connection strings).
    /// </summary>
    public static bool IsSecretName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = path[(path.LastIndexOf('/') + 1)..];
        return SecretPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || SecretNames.Any(secret => string.Equals(name, secret, StringComparison.OrdinalIgnoreCase))
            || SecretExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            || (name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "appsettings.json", StringComparison.OrdinalIgnoreCase));
    }
}
