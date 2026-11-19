using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 that speaks the parts of the Ollama API the app uses (<c>/</c>,
/// <c>/api/tags</c>, <c>/api/version</c>, <c>/api/chat</c> with NDJSON streaming). Test-only (AI-002 acceptance:
/// "integration test against a stub HTTP server"); one request per connection.
/// </summary>
internal sealed class StubOllamaServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public StubOllamaServer()
    {
        _listener.Start();
        Endpoint = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture)}";
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Base URL, e.g. <c>http://127.0.0.1:52344</c>.</summary>
    public string Endpoint { get; }

    public IReadOnlyList<string> Models { get; set; } = ["llama3.2:3b", "qwen2.5-coder:7b"];

    /// <summary>Text pieces streamed as the assistant's answer.</summary>
    public IReadOnlyList<string> AnswerChunks { get; set; } = ["Hello"];

    /// <summary>When set, every API request is answered with this status code.</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Delay before each streamed chunk (cancellation tests).</summary>
    public TimeSpan ChunkDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Requests received: "METHOD path" and the body.</summary>
    public ConcurrentQueue<(string Request, string Body)> Requests { get; } = new();

    /// <summary>Parsed bodies of <c>/api/chat</c> requests.</summary>
    public IEnumerable<JsonDocument> ChatRequests =>
        Requests.Where(r => r.Request == "POST /api/chat").Select(r => JsonDocument.Parse(r.Body));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopping the listener ends the accept loop.
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (request, headers) = await ReadHeadAsync(stream);
                var body = await ReadBodyAsync(stream, headers);
                Requests.Enqueue((request, body));
                await RespondAsync(stream, request);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The client went away (e.g. cancelled the request).
            }
        }
    }

    private async Task RespondAsync(NetworkStream stream, string request)
    {
        if (FailWith is { } status && request != "GET /")
        {
            await WriteAsync(stream, status, "application/json", """{"error":"stub failure"}""");
            return;
        }

        switch (request)
        {
            case "GET /":
            case "HEAD /":
                await WriteAsync(stream, HttpStatusCode.OK, "text/plain", "Ollama is running");
                break;
            case "GET /api/version":
                await WriteAsync(stream, HttpStatusCode.OK, "application/json", """{"version":"0.13.0"}""");
                break;
            case "GET /api/tags":
                var models = string.Join(',', Models.Select(m =>
                    $$$"""{"name":{{{JsonSerializer.Serialize(m)}}},"model":{{{JsonSerializer.Serialize(m)}}},"modified_at":"2026-11-01T10:00:00Z","size":1000,"digest":"abc","details":{"format":"gguf","family":"llama","parameter_size":"3B","quantization_level":"Q4_K_M"}}"""));
                await WriteAsync(stream, HttpStatusCode.OK, "application/json", $$"""{"models":[{{models}}]}""");
                break;
            case "POST /api/chat":
                await StreamChatAsync(stream);
                break;
            default:
                await WriteAsync(stream, HttpStatusCode.NotFound, "application/json", """{"error":"not found"}""");
                break;
        }
    }

    private async Task StreamChatAsync(NetworkStream stream)
    {
        var head = "HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        foreach (var piece in AnswerChunks)
        {
            if (ChunkDelay > TimeSpan.Zero)
            {
                await Task.Delay(ChunkDelay, _stop.Token);
            }

            await WriteChunkAsync(stream,
                $$"""{"model":"m","created_at":"2026-11-19T10:00:00Z","message":{"role":"assistant","content":{{JsonSerializer.Serialize(piece)}}},"done":false}""" + "\n");
        }

        await WriteChunkAsync(stream,
            """{"model":"m","created_at":"2026-11-19T10:00:01Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","total_duration":1000,"prompt_eval_count":120,"eval_count":7}""" + "\n");
        await stream.WriteAsync("0\r\n\r\n"u8.ToArray());
    }

    private static async Task WriteChunkAsync(NetworkStream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(bytes.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n"));
        await stream.WriteAsync(bytes);
        await stream.WriteAsync("\r\n"u8.ToArray());
        await stream.FlushAsync();
    }

    private static async Task WriteAsync(NetworkStream stream, HttpStatusCode status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = string.Create(CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<(string Request, Dictionary<string, string> Headers)> ReadHeadAsync(NetworkStream stream)
    {
        var requestLine = await ReadLineAsync(stream);
        var parts = requestLine.Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var line = await ReadLineAsync(stream); line.Length > 0; line = await ReadLineAsync(stream))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return ($"{parts[0]} {parts[1].Split('?')[0]}", headers);
    }

    private static async Task<string> ReadBodyAsync(NetworkStream stream, Dictionary<string, string> headers)
    {
        using var body = new MemoryStream();
        if (headers.TryGetValue("Content-Length", out var length))
        {
            await CopyExactlyAsync(stream, body, int.Parse(length, CultureInfo.InvariantCulture));
        }
        else if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            for (var size = Convert.ToInt32((await ReadLineAsync(stream)).Split(';')[0], 16); size > 0;
                size = Convert.ToInt32((await ReadLineAsync(stream)).Split(';')[0], 16))
            {
                await CopyExactlyAsync(stream, body, size);
                await ReadLineAsync(stream);
            }

            await ReadLineAsync(stream);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async Task CopyExactlyAsync(NetworkStream from, MemoryStream to, int count)
    {
        var buffer = new byte[count];
        await from.ReadExactlyAsync(buffer);
        await to.WriteAsync(buffer);
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one) == 0)
            {
                throw new IOException("Connection closed.");
            }

            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            }

            bytes.Add(one[0]);
        }
    }
}
