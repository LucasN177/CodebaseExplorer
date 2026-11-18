using LibGit2Sharp;

namespace CodebaseExplorer.TestRepositories.Git;

/// <summary>An author (and committer) of builder-created commits.</summary>
public sealed record GitTestAuthor(string Name, string Email)
{
    public static readonly GitTestAuthor Ada = new("Ada Lovelace", "ada@example.com");

    public static readonly GitTestAuthor Grace = new("Grace Hopper", "grace@example.com");

    public static readonly GitTestAuthor Linus = new("Linus Torvalds", "linus@example.com");

    internal Signature ToSignature(DateTimeOffset when) => new(Name, Email, when);
}
