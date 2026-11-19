using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.AI;

/// <summary>
/// Registration entry point of the AI module. Optional, disabled by default; local-first AI provider abstraction
/// (ADR-008, AI_ARCHITECTURE.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the <see cref="IAiAssistant"/> with its provider gate and the HTTP transport. Registration constructs no
    /// chat client and opens no connection; that happens only behind the gate when AI is enabled.
    /// </summary>
    public static IServiceCollection AddAiModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ => new AiTransport());
        services.AddSingleton<IAiProvider, OllamaProvider>();
        services.AddSingleton<AiProviderGate>();
        services.AddSingleton<IAiAssistant, AiAssistant>();
        return services;
    }
}
