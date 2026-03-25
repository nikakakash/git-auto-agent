using GitAutoAgent.Configuration;
using GitAutoAgent.Helpers;
using GitAutoAgent.Models;
using Microsoft.Extensions.Logging;

namespace GitAutoAgent.Services;

/// <summary>
/// Coordinates the full pipeline: fork → clone → branch → AI changes → commit/push → PR.
/// </summary>
public class OrchestrationService
{
    private readonly GitHubService _github;
    private readonly GitService _git;
    private readonly AiCodeService _ai;
    private readonly AgentSettings _agentSettings;
    private readonly ILogger<OrchestrationService> _logger;

    public OrchestrationService(
        GitHubService github,
        GitService git,
        AiCodeService ai,
        AppSettings settings,
        ILogger<OrchestrationService> logger)
    {
        _github = github;
        _git = git;
        _ai = ai;
        _agentSettings = settings.Agent;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full automation pipeline for the given <paramref name="request"/>.
    /// Each step is logged; on failure the temp directory is cleaned up.
    /// </summary>
    public async Task<AgentResult> RunAsync(TaskRequest request)
    {
        var result = new AgentResult();
        var context = new RepoContext
        {
            UpstreamOwner = request.Owner,
            UpstreamRepo  = request.RepoName,
            BaseBranch    = request.BaseBranch
        };

        // Resolve the working directory to an absolute path
        var workDir = Path.GetFullPath(_agentSettings.WorkingDirectory);
        Directory.CreateDirectory(workDir);

        var runId    = Guid.NewGuid().ToString("N")[..8];
        var localPath = Path.Combine(workDir, $"{request.RepoName}-{runId}");
        context.LocalPath = localPath;

        try
        {
            // ── Step 1: get authenticated user ──────────────────────────────
            context.AuthenticatedUser = await _github.GetAuthenticatedUserAsync();
            Log(result, $"Authenticated as: {context.AuthenticatedUser}");

            // ── Step 2: fork the repository ─────────────────────────────────
            Log(result, $"Forking {request.Owner}/{request.RepoName}…");
            var existingFork = await _github.GetExistingForkCloneUrlAsync(
                request.Owner, request.RepoName);

            if (existingFork is not null)
            {
                context.ForkCloneUrl = existingFork;
                Log(result, $"Using existing fork: {existingFork}");
            }
            else
            {
                context.ForkCloneUrl = await _github.ForkRepositoryAsync(
                    request.Owner, request.RepoName);
                Log(result, $"Fork ready: {context.ForkCloneUrl}");

                // Give GitHub a moment to fully initialise the fork's objects
                await Task.Delay(5_000);
            }

            // ── Step 3: clone the fork locally ──────────────────────────────
            Log(result, $"Cloning fork into {localPath}…");
            _git.Clone(context.ForkCloneUrl, localPath);
            Log(result, "Clone complete.");

            // ── Step 4: create a feature branch ─────────────────────────────
            context.BranchName = GenerateBranchName(request.TaskDescription);
            Log(result, $"Creating branch '{context.BranchName}'…");
            _git.CreateBranchAndCheckout(localPath, context.BranchName);

            // ── Step 5: gather file context ──────────────────────────────────
            Log(result, "Gathering repository context…");
            var fileContents = GatherFileContext(context);
            Log(result, $"Loaded {fileContents.Count} file(s) as AI context.");

            // ── Step 6: AI code generation ───────────────────────────────────
            Log(result, "Calling AI to generate code changes…");
            var aiResponse = await _ai.GenerateChangesAsync(request, context, fileContents);
            Log(result, $"AI proposed {aiResponse.Changes.Count} change(s).");

            // ── Step 7: apply changes ────────────────────────────────────────
            Log(result, "Applying changes to local files…");
            int applied = DiffHelper.ApplyChanges(localPath, aiResponse.Changes);
            Log(result, $"Applied {applied} file operation(s).");

            // ── Step 8: commit & push ────────────────────────────────────────
            Log(result, "Committing and pushing…");
            _git.StageCommitAndPush(localPath, aiResponse.CommitMessage);
            Log(result, "Push complete.");

            // ── Step 9: open pull request ────────────────────────────────────
            Log(result, "Opening pull request…");
            var prUrl = await _github.CreatePullRequestAsync(
                upstreamOwner: request.Owner,
                upstreamRepo:  request.RepoName,
                forkOwner:     context.AuthenticatedUser,
                headBranch:    context.BranchName,
                baseBranch:    request.BaseBranch,
                title:         aiResponse.PrTitle,
                body:          aiResponse.PrDescription);

            result.PullRequestUrl = prUrl;
            result.Success  = true;
            result.Message  = $"Success! PR opened: {prUrl}";
            Log(result, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed");
            result.Success      = false;
            result.Message      = $"Pipeline failed: {ex.Message}";
            result.ErrorDetails = ex.ToString();
            Log(result, $"ERROR: {ex.Message}");
        }
        finally
        {
            // Clean up the temp clone regardless of outcome
            if (Directory.Exists(localPath))
            {
                _logger.LogDebug("Cleaning up temp directory: {Path}", localPath);
                try { FileHelper.DeleteDirectory(localPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not clean up {Path}", localPath);
                }
            }
        }

        return result;
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, string> GatherFileContext(RepoContext context)
    {
        var files = FileHelper.GetRelevantFiles(
            context.LocalPath,
            _agentSettings.MaxFilesToAnalyze,
            _agentSettings.MaxFileSizeBytes);

        var result = new Dictionary<string, string>();

        foreach (var relativePath in files)
        {
            var absolute = Path.Combine(context.LocalPath, relativePath);
            var content  = FileHelper.TryReadFile(absolute, _agentSettings.MaxFileSizeBytes);

            if (content is not null)
            {
                result[relativePath] = content;
                context.AnalyzedFiles.Add(relativePath);
            }
        }

        return result;
    }

    private static string GenerateBranchName(string taskDescription)
    {
        // Sanitise: lowercase, replace spaces/special chars with hyphens, truncate
        var slug = taskDescription
            .ToLowerInvariant()
            .Replace(' ', '-');

        slug = new string(slug
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());

        // Collapse consecutive hyphens
        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        slug = slug.Trim('-');

        if (slug.Length > 50)
            slug = slug[..50].TrimEnd('-');

        return $"auto/{slug}";
    }

    private void Log(AgentResult result, string message)
    {
        _logger.LogInformation("{Message}", message);
        result.StepLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
    }
}
