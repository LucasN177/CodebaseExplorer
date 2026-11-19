using System.Globalization;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Settings;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Reads and writes <see cref="AiSettings"/> as user settings (keys <c>ai.*</c>, DATABASE_SCHEMA.md §4.17). Missing or
/// unreadable values fall back to the defaults, so a damaged setting never enables AI.
/// </summary>
public sealed class AiSettingsStore(IUserSettingsStore store)
{
    public async Task<AiSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var defaults = AiSettings.Default;
        var enabled = await store.GetAsync(UserSettingKeys.AiEnabled, cancellationToken);
        var provider = await store.GetAsync(UserSettingKeys.AiProvider, cancellationToken);
        var endpoint = await store.GetAsync(UserSettingKeys.AiEndpoint, cancellationToken);
        var model = await store.GetAsync(UserSettingKeys.AiModel, cancellationToken);
        var context = await store.GetAsync(UserSettingKeys.AiMaxContextTokens, cancellationToken);
        var temperature = await store.GetAsync(UserSettingKeys.AiTemperature, cancellationToken);
        var consents = await store.GetAsync(UserSettingKeys.AiRemoteConsents, cancellationToken);

        return new AiSettings
        {
            Enabled = string.Equals(enabled, "true", StringComparison.Ordinal),
            ProviderId = string.IsNullOrWhiteSpace(provider) ? defaults.ProviderId : provider,
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? defaults.Endpoint : endpoint,
            ModelId = string.IsNullOrWhiteSpace(model) ? null : model,
            MaxContextTokens = int.TryParse(context, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens)
                && tokens is >= AiSettings.MinContextTokens and <= AiSettings.MaxContextTokensLimit ? tokens : defaults.MaxContextTokens,
            Temperature = double.TryParse(temperature, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value is >= 0 and <= 2 ? value : defaults.Temperature,
            RemoteConsents = SplitConsents(consents),
        };
    }

    /// <summary>
    /// Validates and stores the settings. Changing the endpoint withdraws every remote consent: consent is given for one
    /// endpoint. Returns the stored settings.
    /// </summary>
    public async Task<Result<AiSettings>> SaveAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { } error)
        {
            return error;
        }

        var previous = await LoadAsync(cancellationToken);
        var endpoint = settings.EndpointUri!.ToString().TrimEnd('/');
        var stored = settings with
        {
            ProviderId = settings.ProviderId.Trim(),
            Endpoint = endpoint,
            ModelId = string.IsNullOrWhiteSpace(settings.ModelId) ? null : settings.ModelId.Trim(),
            RemoteConsents = string.Equals(previous.Endpoint.TrimEnd('/'), endpoint, StringComparison.OrdinalIgnoreCase)
                ? settings.RemoteConsents
                : [],
        };

        await store.SetAsync(UserSettingKeys.AiEnabled, stored.Enabled ? "true" : "false", cancellationToken);
        await store.SetAsync(UserSettingKeys.AiProvider, stored.ProviderId, cancellationToken);
        await store.SetAsync(UserSettingKeys.AiEndpoint, stored.Endpoint, cancellationToken);
        await store.SetAsync(UserSettingKeys.AiModel, stored.ModelId ?? string.Empty, cancellationToken);
        await store.SetAsync(UserSettingKeys.AiMaxContextTokens, stored.MaxContextTokens.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await store.SetAsync(UserSettingKeys.AiTemperature, stored.Temperature.ToString("0.##", CultureInfo.InvariantCulture), cancellationToken);
        await store.SetAsync(UserSettingKeys.AiRemoteConsents, string.Join(' ', stored.RemoteConsents), cancellationToken);
        return stored;
    }

    /// <summary>Allows sending context of the repository to the configured (remote) endpoint.</summary>
    public async Task<AiSettings> GrantRemoteConsentAsync(string repositoryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        var settings = await LoadAsync(cancellationToken);
        if (settings.HasRemoteConsent(repositoryId))
        {
            return settings;
        }

        var updated = settings with { RemoteConsents = [.. settings.RemoteConsents, repositoryId] };
        await store.SetAsync(UserSettingKeys.AiRemoteConsents, string.Join(' ', updated.RemoteConsents), cancellationToken);
        return updated;
    }

    private static List<string> SplitConsents(string? value) =>
        [.. (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => id.Length == 32 && Guid.TryParseExact(id, "N", out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
