using GitAutoAgent.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace GitAutoAgent.Services;

/// <summary>Wraps Octokit calls for forking repositories and opening pull requests.</summary>
public class GitHubService
{
    private readonly GitHubClient _client;
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(AppSettings settings, ILogger<GitHubService> logger)
    {
        _settings = settings.GitHub;
        _logger = logger;

        _client = new GitHubClient(new ProductHeaderValue("GitAutoAgent"))
        {
            Credentials = new Credentials(_settings.Token)
        };
    }

    /// <summary>
    /// Returns the login name of the authenticated GitHub user.
    /// </summary>
    public async Task<string> GetAuthenticatedUserAsync()
    {
        var user = await _client.User.Current();
        return user.Login;
    }

    /// <summary>
    /// Forks <paramref name="owner"/>/<paramref name="repo"/> into the authenticated user's
    /// account and waits until the fork is cloneable (up to 60 seconds).
    /// </summary>
    /// <returns>The clone URL of the new fork.</returns>
    public async Task<string> ForkRepositoryAsync(string owner, string repo)
    {
        _logger.LogInformation("Forking {Owner}/{Repo}…", owner, repo);

        var fork = await RetryAsync(() => _client.Repository.Forks.Create(owner, repo, new NewRepositoryFork()));
        _logger.LogInformation("Fork created: {Url}", fork.HtmlUrl);

        // Poll until the fork is ready (GitHub needs a few seconds to copy objects)
        await WaitForForkReadyAsync(fork.Owner.Login, fork.Name);

        return fork.CloneUrl;
    }

    /// <summary>
    /// Creates a pull request from <paramref name="headBranch"/> on the fork
    /// to <paramref name="baseBranch"/> on the upstream repository.
    /// </summary>
    /// <param name="upstreamOwner">Owner of the original (upstream) repo.</param>
    /// <param name="upstreamRepo">Name of the original repo.</param>
    /// <param name="forkOwner">Owner of the fork (the authenticated user).</param>
    /// <param name="headBranch">Branch in the fork to merge.</param>
    /// <param name="baseBranch">Target branch in the upstream repo.</param>
    /// <param name="title">PR title.</param>
    /// <param name="body">PR description (markdown).</param>
    /// <returns>HTML URL of the created pull request.</returns>
    public async Task<string> CreatePullRequestAsync(
        string upstreamOwner,
        string upstreamRepo,
        string forkOwner,
        string headBranch,
        string baseBranch,
        string title,
        string body)
    {
        _logger.LogInformation(
            "Opening PR: {ForkOwner}:{HeadBranch} → {UpstreamOwner}/{UpstreamRepo}:{BaseBranch}",
            forkOwner, headBranch, upstreamOwner, upstreamRepo, baseBranch);

        var pr = await RetryAsync(() => _client.PullRequest.Create(
            upstreamOwner,
            upstreamRepo,
            new NewPullRequest(title, $"{forkOwner}:{headBranch}", baseBranch)
            {
                Body = body
            }));

        _logger.LogInformation("PR opened: {Url}", pr.HtmlUrl);
        return pr.HtmlUrl;
    }

    /// <summary>
    /// Checks whether a fork already exists for the authenticated user.
    /// Returns the clone URL if it does, or null otherwise.
    /// </summary>
    public async Task<string?> GetExistingForkCloneUrlAsync(string upstreamOwner, string repoName)
    {
        try
        {
            var authenticatedUser = await GetAuthenticatedUserAsync();
            var repo = await _client.Repository.Get(authenticatedUser, repoName);
            if (repo.Fork && repo.Parent?.Owner.Login == upstreamOwner)
                return repo.CloneUrl;
        }
        catch (NotFoundException) { /* fork doesn't exist yet */ }

        return null;
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private async Task WaitForForkReadyAsync(string owner, string repo, int timeoutSeconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var r = await _client.Repository.Get(owner, repo);
                if (!string.IsNullOrEmpty(r.CloneUrl))
                {
                    _logger.LogInformation("Fork is ready.");
                    return;
                }
            }
            catch { /* not ready yet */ }

            _logger.LogDebug("Waiting for fork to become available…");
            await Task.Delay(3_000);
        }

        throw new TimeoutException($"Fork {owner}/{repo} was not ready within {timeoutSeconds}s.");
    }

    /// <summary>Simple retry wrapper for GitHub API calls that may hit rate limits.</summary>
    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts = 3)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try { return await action(); }
            catch (RateLimitExceededException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(30 * attempt));
            }
            catch (ApiException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
            }
        }

        return await action(); // final attempt — let it throw
    }
}
