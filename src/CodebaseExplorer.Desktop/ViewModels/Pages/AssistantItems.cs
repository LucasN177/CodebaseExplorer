using System.Collections.ObjectModel;
using System.Globalization;
using CodebaseExplorer.Contracts.AI;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>A conversation in the assistant's history list.</summary>
public sealed record AiConversationItem(AiConversationDto Conversation)
{
    public string Id => Conversation.Id;

    public string Title => Conversation.Title;

    public string Detail => string.Create(CultureInfo.InvariantCulture,
        $"{Conversation.UpdatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC · {Conversation.ModelId}{(Conversation.IsRemote ? " · remote" : string.Empty)}");

    public override string ToString() => Title;
}

/// <summary>A citation of an answer: verified ones open their source or graph node; unverified ones are flagged.</summary>
public sealed record AiCitationItem(AiCitationDto Citation)
{
    public string Label
    {
        get
        {
            var location = Citation.Path is { } path && Citation.StartLine is { } start
                ? string.Create(CultureInfo.InvariantCulture, $"{path}:{start}-{Citation.EndLine ?? start}")
                : Citation.ElementId is not null ? "model element" : null;
            return Citation.Key is not null && location is not null ? $"{Citation.Text} {location}" : Citation.Text;
        }
    }

    public bool Verified => Citation.Verified;

    public bool Unverified => !Citation.Verified;

    public string ToolTip => Citation.Verified
        ? (CanOpenSource ? "Show the cited lines" : "Show the element in the graph")
        : $"Unverified: {Citation.Problem}";

    public bool CanOpenSource => Citation.Verified && Citation.Path is not null && Citation.StartLine is not null;

    public bool CanShowInGraph => Citation.Verified && Citation.ElementId is not null;
}

/// <summary>A message of the conversation; assistant text is shown as plain-text Markdown blocks.</summary>
public sealed partial class AiMessageItem : ObservableObject
{
    private AiMessageItem(bool isUser)
    {
        IsUser = isUser;
    }

    public bool IsUser { get; }

    public bool IsAssistant => !IsUser;

    /// <summary>The answer while it streams (plain text).</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplete))]
    public partial bool IsStreaming { get; private set; }

    public bool IsComplete => !IsStreaming;

    public ObservableCollection<MarkdownBlock> Blocks { get; } = [];

    public IReadOnlyList<AiCitationItem> Citations { get; private set; } = [];

    public bool HasCitations => Citations.Count > 0;

    /// <summary>The context manifest as lines, e.g. <c>[S1] snippet src/a.cs:1-40 — search match</c>.</summary>
    public IReadOnlyList<string> Context { get; private set; } = [];

    public bool HasContext => Context.Count > 0;

    /// <summary>E.g. <c>AI-generated · 3 citations (1 unverified) · 12 context entries</c>.</summary>
    public string? Meta { get; private set; }

    public static AiMessageItem Question(string text)
    {
        var item = new AiMessageItem(isUser: true) { Text = text };
        item.Blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, text));
        return item;
    }

    public static AiMessageItem Pending() => new(isUser: false) { IsStreaming = true };

    public static AiMessageItem From(AiMessageDto message)
    {
        if (message.Role != AiMessageDto.AssistantRole)
        {
            return Question(message.Content);
        }

        var item = new AiMessageItem(isUser: false);
        item.Complete(message);
        return item;
    }

    /// <summary>Appends streamed text; ignored once the answer is complete.</summary>
    public void Append(string piece)
    {
        if (IsStreaming)
        {
            Text += piece;
        }
    }

    /// <summary>Replaces the streamed text with the stored answer, its citations and its context manifest.</summary>
    public void Complete(AiMessageDto message)
    {
        ArgumentNullException.ThrowIfNull(message);
        IsStreaming = false;
        Text = message.Content;
        Blocks.Clear();
        foreach (var block in MarkdownBlocks.Parse(message.Content))
        {
            Blocks.Add(block);
        }

        Citations = [.. message.Citations.Select(c => new AiCitationItem(c))];
        Context = [.. message.Context.Select(Describe)];
        var unverified = message.Citations.Count(c => !c.Verified);
        var citations = message.Citations.Count;
        var unverifiedText = unverified > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({unverified} unverified)") : string.Empty;
        Meta = string.Create(CultureInfo.InvariantCulture,
            $"AI-generated · {citations} {(citations == 1 ? "citation" : "citations")}{unverifiedText} · {message.Context.Count} context entries");
        OnPropertyChanged(nameof(Citations));
        OnPropertyChanged(nameof(HasCitations));
        OnPropertyChanged(nameof(Context));
        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(Meta));
    }

    private static string Describe(AiContextEntryDto entry)
    {
        var location = entry.Path is { } path && entry.StartLine is { } start
            ? string.Create(CultureInfo.InvariantCulture, $" {path}:{start}-{entry.EndLine ?? start}")
            : string.Empty;
        return $"[{entry.Key}] {entry.Kind}{location} — {entry.Label}";
    }
}
