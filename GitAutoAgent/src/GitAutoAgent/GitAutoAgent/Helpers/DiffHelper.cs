using GitAutoAgent.Models;
using System.Text.Json;

namespace GitAutoAgent.Helpers;

/// <summary>Parses and applies AI-generated file changes to the local clone.</summary>
public static class DiffHelper
{
    /// <summary>
    /// Applies a list of <see cref="FileChange"/> objects to the local repository.
    /// </summary>
    /// <param name="localRepoPath">Absolute path to the repository root.</param>
    /// <param name="changes">Changes returned by the AI service.</param>
    /// <returns>Number of files successfully modified.</returns>
    public static int ApplyChanges(string localRepoPath, IEnumerable<FileChange> changes)
    {
        int count = 0;

        foreach (var change in changes)
        {
            var absolutePath = Path.Combine(localRepoPath, change.FilePath.TrimStart('/'));

            switch (change.Action.ToLowerInvariant())
            {
                case "create":
                case "modify":
                    FileHelper.WriteFile(absolutePath, change.Content ?? string.Empty);
                    count++;
                    break;

                case "delete":
                    FileHelper.DeleteFile(absolutePath);
                    count++;
                    break;

                default:
                    // Unknown action — skip silently
                    break;
            }
        }

        return count;
    }

    /// <summary>
    /// Deserialises the raw JSON string returned by the AI into an <see cref="AiCodeResponse"/>.
    /// Returns null if the JSON cannot be parsed.
    /// </summary>
    /// <param name="json">Raw JSON string from the AI service.</param>
    public static AiCodeResponse? ParseAiResponse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AiCodeResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}

// ── DTOs used by DiffHelper & AiCodeService ──────────────────────────────────

/// <summary>Top-level response object returned by the AI.</summary>
public class AiCodeResponse
{
    public List<FileChange> Changes { get; set; } = new();
    public string CommitMessage { get; set; } = "AI-generated changes";
    public string PrTitle { get; set; } = "Automated changes";
    public string PrDescription { get; set; } = string.Empty;
}

/// <summary>Represents a single file operation requested by the AI.</summary>
public class FileChange
{
    /// <summary>Path relative to the repository root.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>"create", "modify", or "delete".</summary>
    public string Action { get; set; } = "modify";

    /// <summary>Full new content for create/modify operations.</summary>
    public string? Content { get; set; }
}
