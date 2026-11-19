using System.Net;
using System.Net.Sockets;
using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>AI-002: the Ollama provider against a stub Ollama server on loopback (no real Ollama needed).</summary>
public sealed class OllamaProviderTests : IAsyncDisposable
{
    private readonly StubOllamaServer _server = new();
    private readonly OllamaProvider _provider = new(new AiTransport(), NullLogger<OllamaProvider>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private AiSettings Settings => AiSettings.Default with { Enabled = true, Endpoint = _server.Endpoint, ModelId = "llama3.2:3b" };

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [Fact]
    public async Task Check_ListsTheModels_AndTheVersion()
    {
        var status = (await _provider.CheckAsync(Settings, Token)).Value;

        Assert.Equal(["llama3.2:3b", "qwen2.5-coder:7b"], status.Models);
        Assert.Equal("0.13.0", status.Version);
        Assert.False(status.IsRemote);
        Assert.Equal(_server.Endpoint, status.Endpoint);
        Assert.DoesNotContain(_server.Requests, r => r.Request == "POST /api/chat");
    }

    [Fact]
    public async Task Check_ServerError_IsReportedAsUnavailable()
    {
        _server.FailWith = HttpStatusCode.InternalServerError;

        var result = await _provider.CheckAsync(Settings, Token);

        Assert.Equal(AiErrorCodes.ProviderUnavailable, result.Error.Code);
        Assert.Contains(_server.Endpoint, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_NothingListening_IsReportedAsUnavailable()
    {
        var unused = new TcpListener(IPAddress.Loopback, 0);
        unused.Start();
        var port = ((IPEndPoint)unused.LocalEndpoint).Port;
        unused.Stop();

        var result = await _provider.CheckAsync(Settings with { Endpoint = $"http://127.0.0.1:{port}" }, Token);

        Assert.Equal(AiErrorCodes.ProviderUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task ChatClient_StreamsTheAnswer_WithTheConfiguredModelAndTemperature()
    {
        _server.AnswerChunks = ["The answer ", "is [F1]."];
        using var client = _provider.CreateChatClient(Settings);

        var text = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.System, "rules"), new ChatMessage(ChatRole.User, "question?")],
            new ChatOptions { Temperature = 0.3f },
            Token))
        {
            text.Add(update.Text);
        }

        Assert.Equal("The answer is [F1].", string.Concat(text));
        var request = Assert.Single(_server.ChatRequests).RootElement;
        Assert.Equal("llama3.2:3b", request.GetProperty("model").GetString());
        Assert.Equal("question?", request.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal(0.3, request.GetProperty("options").GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task ChatClient_CanBeCancelledWhileStreaming()
    {
        _server.AnswerChunks = ["a", "b", "c", "d"];
        _server.ChunkDelay = TimeSpan.FromMilliseconds(300);
        using var client = _provider.CreateChatClient(Settings);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        // OllamaSharp ends the stream quietly on cancellation (found in AI-002) instead of throwing; callers check the
        // token themselves. What matters: no further chunks arrive.
        var received = 0;
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], null, cancel.Token))
            {
                received++;
                await cancel.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Also acceptable.
        }

        Assert.Equal(1, received);
    }
}
