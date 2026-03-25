namespace GitAutoAgent.Models;

/// <summary>Runtime metadata about the forked repository and local working copy.</summary>
public class RepoContext
{
    /// <summary>GitHub login of the authenticated user (fork owner).</summary>
    public string AuthenticatedUser { get; set; } = string.Empty;

    /// <summary>Original repository owner.</summary>
    public string UpstreamOwner { get; set; } = string.Empty;

    /// <summary>Original repository name.</summary>
    public string UpstreamRepo { get; set; } = string.Empty;

    /// <summary>Clone URL of the user's fork.</summary>
    public string ForkCloneUrl { get; set; } = string.Empty;

    /// <summary>Absolute path to the local clone of the fork.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Name of the feature branch created for this task.</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Base branch of the upstream repo (e.g. "main").</summary>
    public string BaseBranch { get; set; } = "main";

    /// <summary>File paths (relative to <see cref="LocalPath"/>) selected for AI analysis.</summary>
    public List<string> AnalyzedFiles { get; set; } = new();
}
