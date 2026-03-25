using GitAutoAgent.Configuration;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;

namespace GitAutoAgent.Services;

/// <summary>Wraps LibGit2Sharp for local git operations (clone, branch, commit, push).</summary>
public class GitService
{
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitService> _logger;

    public GitService(AppSettings settings, ILogger<GitService> logger)
    {
        _settings = settings.GitHub;
        _logger = logger;
    }

    /// <summary>
    /// Clones <paramref name="cloneUrl"/> into <paramref name="localPath"/> using
    /// the configured GitHub token as credentials.
    /// </summary>
    /// <param name="cloneUrl">HTTPS clone URL of the fork.</param>
    /// <param name="localPath">Destination directory (must not already exist).</param>
    public void Clone(string cloneUrl, string localPath)
    {
        _logger.LogInformation("Cloning {Url} → {Path}", cloneUrl, localPath);

        var options = new CloneOptions();
        options.FetchOptions.CredentialsProvider = BuildCredentialsHandler();

        Repository.Clone(cloneUrl, localPath, options);
        _logger.LogInformation("Clone complete.");
    }

    /// <summary>
    /// Creates a new branch named <paramref name="branchName"/> from HEAD and checks it out.
    /// </summary>
    /// <param name="localPath">Absolute path to the local clone.</param>
    /// <param name="branchName">Name for the new branch.</param>
    public void CreateBranchAndCheckout(string localPath, string branchName)
    {
        _logger.LogInformation("Creating branch '{Branch}'", branchName);

        using var repo = new Repository(localPath);

        var branch = repo.CreateBranch(branchName);
        Commands.Checkout(repo, branch);

        _logger.LogInformation("Checked out branch '{Branch}'", branchName);
    }

    /// <summary>
    /// Stages all changes, creates a commit, and pushes the current branch to origin.
    /// </summary>
    /// <param name="localPath">Absolute path to the local clone.</param>
    /// <param name="commitMessage">Commit message.</param>
    /// <param name="authorName">Git author name.</param>
    /// <param name="authorEmail">Git author email.</param>
    public void StageCommitAndPush(
        string localPath,
        string commitMessage,
        string authorName = "GitAutoAgent",
        string authorEmail = "gitautoagent@noreply.local")
    {
        _logger.LogInformation("Staging and committing changes…");

        using var repo = new Repository(localPath);

        // Stage everything
        Commands.Stage(repo, "*");

        var status = repo.RetrieveStatus();
        if (!status.IsDirty)
        {
            _logger.LogWarning("No changes to commit.");
            return;
        }

        var signature = new Signature(authorName, authorEmail, DateTimeOffset.UtcNow);
        repo.Commit(commitMessage, signature, signature);
        _logger.LogInformation("Committed: {Message}", commitMessage);

        // Push
        var remote = repo.Network.Remotes["origin"];
        var refSpec = $"refs/heads/{repo.Head.FriendlyName}:refs/heads/{repo.Head.FriendlyName}";

        _logger.LogInformation("Pushing branch '{Branch}' to origin…", repo.Head.FriendlyName);

        repo.Network.Push(remote, refSpec, new PushOptions
        {
            CredentialsProvider = BuildCredentialsHandler()
        });

        _logger.LogInformation("Push complete.");
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private CredentialsHandler BuildCredentialsHandler() =>
        (url, user, types) => new UsernamePasswordCredentials
        {
            Username = _settings.Token,
            Password = string.Empty
        };
}
