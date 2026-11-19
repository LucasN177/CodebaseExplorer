using CodebaseExplorer.Desktop.ViewModels.Pages;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>AI-006: AI answers become plain-text blocks; nothing is interpreted as HTML.</summary>
public sealed class MarkdownBlocksTests
{
    [Fact]
    public void Blocks_AreRecognized()
    {
        var blocks = MarkdownBlocks.Parse("# Title\n\nFirst **bold** line\nsecond line\n\n- one\n* two\n3. three\n\n```csharp\nvar x = 1;\n```\n");

        Assert.Equal(
        [
            new MarkdownBlock(MarkdownBlockKind.Heading, "Title"),
            new MarkdownBlock(MarkdownBlockKind.Paragraph, "First bold line\nsecond line"),
            new MarkdownBlock(MarkdownBlockKind.Bullet, "• one"),
            new MarkdownBlock(MarkdownBlockKind.Bullet, "• two"),
            new MarkdownBlock(MarkdownBlockKind.Bullet, "3. three"),
            new MarkdownBlock(MarkdownBlockKind.Code, "var x = 1;", "csharp"),
        ], blocks);
    }

    [Fact]
    public void HtmlAndLinks_StayLiteral()
    {
        const string Html = "<b>x</b> <img src=x onerror=alert(1)> ![img](http://e/x.png) [a](javascript:alert(1))";

        Assert.Equal([new MarkdownBlock(MarkdownBlockKind.Paragraph, Html)], MarkdownBlocks.Parse(Html));
    }

    [Fact]
    public void UnclosedFence_KeepsTheRestAsCode() =>
        Assert.Equal([new MarkdownBlock(MarkdownBlockKind.Code, "a\nb", null)], MarkdownBlocks.Parse("```\na\nb"));
}
