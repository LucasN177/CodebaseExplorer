using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.IntegrationTests.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// AI-006: the assistant page with the real composition root and a stub Ollama server — streamed answer, citations,
/// context manifest, history, cancellation, consent, and model output shown as text only.
/// </summary>
public sealed class AssistantPageViewModelTests : IAsyncLifetime
{
    private readonly StubOllamaServer _server = new();
    private readonly TestApplication _app = new();
    private readonly NamedFixtureCopy _repository = new("dotnet-layered");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private AssistantPageViewModel Page => _app.Services.GetRequiredService<AssistantPageViewModel>();

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        var stored = await _repository.ImportAsync(_app.Services, Token);
        var open = (await _app.Services.GetRequiredService<OpenRepository>().ExecuteAsync(stored.Repository.Id.ToString(), Token)).Value;
        _app.Services.GetRequiredService<RepositorySession>().SetCurrent(open);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _server.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task AiOff_ExplainsHowToEnableIt_AndCannotAsk()
    {
        var page = Page;
        await page.ActivateAsync();

        Assert.False(page.IsAiEnabled);
        Assert.StartsWith("The AI assistant is off", page.AiStatus, StringComparison.Ordinal);
        page.Question = "anything?";
        Assert.False(page.AskCommand.CanExecute(null));
    }

    [Fact]
    public async Task Asking_ShowsTheAnswerWithCitationsAndContext_AndKeepsItInTheHistory()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["## Orders\n", "- placed by `PlaceOrderHandler` [F1]\n- invented [S42]"];
        var page = Page;
        await page.ActivateAsync();

        page.Question = "How are orders placed?";
        await page.AskCommand.ExecuteAsync(null);

        Assert.Null(page.StatusMessage);
        Assert.Null(page.Question);
        Assert.Equal(2, page.Messages.Count);
        var answer = page.Messages[1];
        Assert.True(answer.IsAssistant);
        Assert.False(answer.IsStreaming);
        Assert.Equal([MarkdownBlockKind.Heading, MarkdownBlockKind.Bullet, MarkdownBlockKind.Bullet], answer.Blocks.Select(b => b.Kind));
        Assert.Equal([true, false], answer.Citations.Select(c => c.Verified));
        Assert.True(answer.HasContext);
        Assert.StartsWith("AI-generated · 2 citations (1 unverified)", answer.Meta, StringComparison.Ordinal);
        var conversation = Assert.Single(page.Conversations);
        Assert.Same(conversation, page.SelectedConversation);

        // A new page instance (e.g. after a restart) shows the stored conversation.
        page.NewConversationCommand.Execute(null);
        Assert.Empty(page.Messages);
        page.SelectedConversation = page.Conversations[0];
        await WaitUntilAsync(() => page.Messages.Count == 2);
        Assert.Equal(answer.Text, page.Messages[1].Text);
    }

    [Fact]
    public async Task ModelOutputWithHtml_IsShownAsLiteralText()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["<img src=\"https://evil.example/x.png\" onerror=\"alert(1)\"> <script>steal()</script> [link](https://evil.example)"];
        var page = Page;
        await page.ActivateAsync();

        page.Question = "Show me something";
        await page.AskCommand.ExecuteAsync(null);

        var block = Assert.Single(page.Messages[1].Blocks);
        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(_server.AnswerChunks[0], block.Text);
    }

    [Fact]
    public async Task VerifiedFileCitation_ShowsTheCitedLines()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["See src/Shop.Application/Orders/PlaceOrderHandler.cs:5-7."];
        var page = Page;
        await page.ActivateAsync();
        page.Question = "Where?";
        await page.AskCommand.ExecuteAsync(null);

        var citation = Assert.Single(page.Messages[1].Citations);
        await page.OpenCitationCommand.ExecuteAsync(citation);

        Assert.Equal("src/Shop.Application/Orders/PlaceOrderHandler.cs", page.SnippetPath);
        Assert.Equal([5, 6, 7], page.SnippetLines.Where(l => l.IsEvidence).Select(l => l.Number));
    }

    [Fact]
    public async Task Cancelling_RemovesTheQuestion_AndSavesNothing()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["a", "b", "c", "d", "e"];
        _server.ChunkDelay = TimeSpan.FromMilliseconds(400);
        var page = Page;
        await page.ActivateAsync();

        page.Question = "Slow question?";
        var asking = page.AskCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => page.IsBusy && _server.ChatRequests.Any());
        page.CancelCommand.Execute(null);
        await asking;

        Assert.Empty(page.Messages);
        Assert.Equal("Cancelled. Nothing was saved.", page.StatusMessage);
        Assert.Equal("Slow question?", page.Question);
        Assert.False(page.IsBusy);
        Assert.Empty(page.Conversations);
    }

    [Fact]
    public async Task RemoteEndpoint_AsksForConsent_BeforeSendingAnything()
    {
        await EnableAsync("http://gpu-box.invalid:11434");
        var page = Page;
        await page.ActivateAsync();
        Assert.True(page.IsRemote);

        page.Question = "q?";
        await page.AskCommand.ExecuteAsync(null);

        Assert.NotNull(page.ConsentText);
        Assert.Empty(page.Messages);
        Assert.True(page.AllowRemoteCommand.CanExecute(null));
    }

    private async Task EnableAsync(string endpoint) =>
        await _app.Services.GetRequiredService<AiSettingsStore>().SaveAsync(
            AiSettings.Default with { Enabled = true, Endpoint = endpoint, ModelId = "llama3.2:3b" }, Token);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25, Token);
        }

        Assert.True(condition());
    }
}
