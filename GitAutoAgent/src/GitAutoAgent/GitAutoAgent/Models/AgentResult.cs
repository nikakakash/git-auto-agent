namespace GitAutoAgent.Models;

/// <summary>Outcome of a full agent run.</summary>
public class AgentResult
{
    /// <summary>Whether the pipeline completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>URL of the opened pull request (null on failure).</summary>
    public string? PullRequestUrl { get; set; }

    /// <summary>Human-readable status summary.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Ordered log of completed pipeline steps.</summary>
    public List<string> StepLogs { get; set; } = new();

    /// <summary>Error details when <see cref="Success"/> is false.</summary>
    public string? ErrorDetails { get; set; }
}
