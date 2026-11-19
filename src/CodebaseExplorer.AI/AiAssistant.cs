using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.AI;

/// <summary>
/// <see cref="IAiAssistant"/> of the AI module. Every operation passes the <see cref="AiProviderGate"/> first.
/// </summary>
internal sealed class AiAssistant(AiProviderGate gate) : IAiAssistant
{
    public async Task<Result<AiProviderStatus>> CheckProviderAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (!gate.ForConnection(settings).TryGetValue(out var provider, out var error))
        {
            return error;
        }

        return await provider.CheckAsync(settings, cancellationToken);
    }
}
