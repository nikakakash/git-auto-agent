namespace GitAutoAgent.Configuration;

/// <summary>Strongly-typed configuration root.</summary>
public class AppSettings
{
    public GitHubSettings GitHub { get; set; } = new();
    public AnthropicSettings Anthropic { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

/// <summary>GitHub API configuration.</summary>
public class GitHubSettings
{
    public string Token { get; set; } = string.Empty;
    public string DefaultBaseBranch { get; set; } = "main";
}

/// <summary>Anthropic / Claude API configuration.</summary>
public class AnthropicSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-5";
    public int MaxTokens { get; set; } = 8096;
}

/// <summary>Agent behaviour configuration.</summary>
public class AgentSettings
{
    public string WorkingDirectory { get; set; } = "./temp";
    public int MaxFilesToAnalyze { get; set; } = 20;
    public long MaxFileSizeBytes { get; set; } = 50_000;
}
