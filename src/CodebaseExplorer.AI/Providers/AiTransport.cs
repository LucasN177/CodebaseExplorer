namespace CodebaseExplorer.AI.Providers;

/// <summary>
/// The only place the AI module creates HTTP clients, so tests can replace the handler with one that proves no request
/// is made (SECURITY.md §9, "No network with AI disabled"). Redirects and cookies are off: an endpoint must not be able
/// to send the request (and repository context) elsewhere.
/// </summary>
internal sealed class AiTransport(Func<HttpMessageHandler> createHandler)
{
    public AiTransport()
        : this(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
    }

    /// <summary>An HTTP client for the endpoint; the caller disposes it (and with it the handler).</summary>
    public HttpClient CreateClient(Uri endpoint, TimeSpan timeout) =>
        new(createHandler(), disposeHandler: true) { BaseAddress = endpoint, Timeout = timeout };
}
