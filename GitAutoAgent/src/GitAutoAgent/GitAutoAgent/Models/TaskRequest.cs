namespace GitAutoAgent.Models;

/// <summary>Represents the user's input — what repo to work on and what to do.</summary>
public class TaskRequest
{
    /// <summary>GitHub repo in "owner/name" format (e.g. "octocat/hello-world").</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>Natural-language description of the changes to make.</summary>
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>Base branch of the upstream repo to target with the PR (default: "main").</summary>
    public string BaseBranch { get; set; } = "main";

    /// <summary>Convenience property that splits <see cref="Repository"/> into owner.</summary>
    public string Owner => Repository.Split('/')[0];

    /// <summary>Convenience property that splits <see cref="Repository"/> into repo name.</summary>
    public string RepoName => Repository.Split('/')[1];
}
