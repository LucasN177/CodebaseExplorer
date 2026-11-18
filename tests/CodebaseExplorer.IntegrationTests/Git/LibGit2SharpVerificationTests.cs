using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories;
using LibGit2Sharp;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>
/// Proof code of spike GIT-006 (GIT_INTEGRATION.md §3 "Verification results"): facts about LibGit2Sharp 0.32.0 that
/// the Git module relies on. Runs on every CI OS, so it also proves that the native library loads on each of them.
/// </summary>
public sealed class LibGit2SharpVerificationTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void NativeLibrary_LoadsAndSupportsHttps()
    {
        var version = GlobalSettings.Version;

        Assert.StartsWith("0.32.0", version.InformationalVersion, StringComparison.Ordinal);
        Assert.True(version.Features.HasFlag(BuiltInFeatures.Https));
        Assert.True(version.Features.HasFlag(BuiltInFeatures.Threads));
    }

    [Fact]
    public void LocalOperations_RunNoHookFsmonitorOrFilterDriver()
    {
        var path = _temp.CreateDirectory("hostile");
        Repository.Init(path);
        WriteHostileConfiguration(path);
        File.WriteAllText(Path.Combine(path, ".gitattributes"), "*.txt filter=evil diff=evil\n");
        File.WriteAllText(Path.Combine(path, "a.txt"), "one\n");

        using (var repository = new Repository(path))
        {
            var signature = new Signature("Ada", "ada@example.com", DateTimeOffset.UnixEpoch.AddYears(50));
            Commands.Stage(repository, "*");
            repository.Commit("first", signature, signature);
            File.AppendAllText(Path.Combine(path, "a.txt"), "two\n");
            Commands.Stage(repository, "*");
            var second = repository.Commit("second", signature, signature);

            // Everything the reader and the history walk do: status, refs, remotes, commit walk, diffs.
            _ = repository.RetrieveStatus(new StatusOptions());
            _ = repository.Head.FriendlyName;
            _ = repository.Network.Remotes.ToList();
            foreach (var commit in repository.Commits)
            {
                using var patch = repository.Diff.Compare<Patch>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree);
                _ = patch.LinesAdded;
            }

            // Operations only an app-managed clone performs (checkout of a branch).
            var branch = repository.CreateBranch("feature", second);
            Commands.Checkout(repository, branch);
            File.Delete(Path.Combine(path, "a.txt"));
            repository.CheckoutPaths("feature", ["a.txt"], new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        }

        Assert.Empty(ZeroExecutionHarness.Markers(_temp.FullPath));
    }

    [Fact]
    public void ShallowClone_IsNotSupportedByTheLocalTransport()
    {
        // Shallow clones work over HTTPS (verified manually and in ShallowHttpsClone_OfPublicRepository); the local
        // transport used by clone tests rejects them, which is one more reason why the product clones full history.
        var source = _temp.CreateDirectory("source");
        Repository.Init(source);
        using (var repository = new Repository(source))
        {
            File.WriteAllText(Path.Combine(source, "a.txt"), "one\n");
            Commands.Stage(repository, "*");
            var signature = new Signature("Ada", "ada@example.com", DateTimeOffset.UnixEpoch.AddYears(50));
            repository.Commit("first", signature, signature);
        }

        var exception = Assert.Throws<LibGit2SharpException>(() => Repository.Clone(
            new Uri(source).AbsoluteUri, _temp.PathOf("shallow"), new CloneOptions { FetchOptions = { Depth = 1 } }));
        Assert.Contains("shallow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Network")]
    public void ShallowHttpsClone_OfPublicRepository()
    {
        var target = _temp.PathOf("network");

        Repository.Clone("https://github.com/libgit2/libgit2sharp.git", target, new CloneOptions { FetchOptions = { Depth = 1 }, Checkout = false });

        using var repository = new Repository(target);
        Assert.True(repository.Info.IsShallow);
        Assert.Single(repository.Commits);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Network")]
    public void HttpsClone_WithToken_UsesTheCredentialsCallbackOnly()
    {
        // Set CODEBASE_EXPLORER_TEST_GIT_URL (a private HTTPS repository) and CODEBASE_EXPLORER_TEST_GIT_TOKEN to run.
        var url = Environment.GetEnvironmentVariable("CODEBASE_EXPLORER_TEST_GIT_URL");
        var token = Environment.GetEnvironmentVariable("CODEBASE_EXPLORER_TEST_GIT_TOKEN");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
        {
            Assert.Skip("CODEBASE_EXPLORER_TEST_GIT_URL and CODEBASE_EXPLORER_TEST_GIT_TOKEN are not set.");
        }

        var target = _temp.PathOf("private");
        var options = new CloneOptions { Checkout = false };
        options.FetchOptions.CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials { Username = "oauth2", Password = token };

        Repository.Clone(url, target, options);

        var config = File.ReadAllText(Path.Combine(target, ".git", "config"));
        Assert.DoesNotContain(token, config, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration that runs a command for every hook, fsmonitor query and filter/diff driver if any of them were
    /// executed. Commands use relative marker paths: Git runs them in the working tree.
    /// </summary>
    private static void WriteHostileConfiguration(string path)
    {
        var marker = ZeroExecutionHarness.MarkerPrefix;
        File.AppendAllText(Path.Combine(path, ".git", "config"), $"""

            [core]
            	fsmonitor = touch {marker}fsmonitor
            	hooksPath = .git/hooks
            	sshCommand = touch {marker}ssh
            [filter "evil"]
            	clean = touch {marker}clean
            	smudge = touch {marker}smudge
            	process = touch {marker}process
            	required = true
            [diff "evil"]
            	textconv = touch {marker}textconv
            	command = touch {marker}diff
            [credential]
            	helper = !touch {marker}credential
            """);

        var hooks = Directory.CreateDirectory(Path.Combine(path, ".git", "hooks")).FullName;
        foreach (var hook in new[] { "pre-commit", "prepare-commit-msg", "commit-msg", "post-commit", "post-checkout", "reference-transaction", "post-index-change", "fsmonitor-watchman" })
        {
            File.WriteAllText(Path.Combine(hooks, hook), $"#!/bin/sh\ntouch {marker}{hook}\n");
        }
    }
}
