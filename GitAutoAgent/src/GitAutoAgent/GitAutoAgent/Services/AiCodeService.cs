using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitAutoAgent.Configuration;
using GitAutoAgent.Helpers;
using GitAutoAgent.Models;
using Microsoft.Extensions.Logging;

namespace GitAutoAgent.Services;

/// <summary>Calls the Anthropic Claude API to analyse the codebase and produce file changes.</summary>
public class AiCodeService
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly AnthropicSettings _settings;
    private readonly ILogger<AiCodeService> _logger;

    public AiCodeService(
        IHttpClientFactory httpClientFactory,
        AppSettings settings,
        ILogger<AiCodeService> logger)
    {
        _http = httpClientFactory.CreateClient("Anthropic");
        _settings = settings.Anthropic;
        _logger = logger;
    }

    /// <summary>
    /// Sends the task description and repo context to Claude and returns a structured
    /// <see cref="AiCodeResponse"/> with the file changes to apply.
    /// </summary>
    /// <param name="request">The original task request.</param>
    /// <param name="context">Runtime repo metadata including which files were loaded.</param>
    /// <param name="fileContents">Dictionary of relative path → file text for context.</param>
    public async Task<AiCodeResponse> GenerateChangesAsync(
        TaskRequest request,
        RepoContext context,
        IReadOnlyDictionary<string, string> fileContents)
    {
        _logger.LogInformation("Calling Claude API ({Model})…", _settings.Model);

        var userMessage = BuildUserMessage(request, context, fileContents);
        var requestBody = BuildRequestBody(userMessage);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint);
        httpRequest.Headers.Add("x-api-key", _settings.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic API returned {(int)response.StatusCode}: {responseBody}");

        var aiText = ExtractTextFromResponse(responseBody);
        _logger.LogDebug("Raw AI response length: {Len} chars", aiText.Length);

        var parsed = DiffHelper.ParseAiResponse(ExtractJson(aiText));

        if (parsed is null || parsed.Changes.Count == 0)
            throw new InvalidOperationException(
                $"AI response could not be parsed or contained no changes.\nRaw: {aiText}");

        _logger.LogInformation(
            "AI produced {Count} file change(s). Commit: \"{Msg}\"",
            parsed.Changes.Count, parsed.CommitMessage);

        return parsed;
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private object BuildRequestBody(string userMessage) => new
    {
        model = _settings.Model,
        max_tokens = _settings.MaxTokens,
        system = SystemPrompt(),
        messages = new[]
        {
            new { role = "user", content = userMessage }
        }
    };

    private static string SystemPrompt() => """
        You are an expert software engineer assistant. You will be given:
        1. A GitHub repository's file structure and the contents of its most relevant files.
        2. A task description explaining what changes to make.

        Analyse the codebase carefully, then respond with ONLY a valid JSON object — no markdown
        fences, no explanation text — matching this exact schema:

        {
          "changes": [
            {
              "filePath": "relative/path/to/file.ext",
              "action": "create" | "modify" | "delete",
              "content": "full new file content (omit for delete)"
            }
          ],
          "commitMessage": "Short imperative summary of changes",
          "prTitle": "Clear pull-request title",
          "prDescription": "Markdown description with ## sections"
        }

        Rules:
        - For 'create' and 'modify', always include the COMPLETE new file content.
        - Preserve existing code style, indentation, and conventions.
        - Only touch the files necessary for the task.
        - The prDescription should explain what changed and why.
        """;

    private static string BuildUserMessage(
        TaskRequest request,
        RepoContext context,
        IReadOnlyDictionary<string, string> fileContents)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Repository: {request.Owner}/{request.RepoName}");
        sb.AppendLine($"## Task: {request.TaskDescription}");
        sb.AppendLine();
        sb.AppendLine("### Files included for context:");

        foreach (var (path, content) in fileContents)
        {
            sb.AppendLine();
            sb.AppendLine($"#### {path}");
            sb.AppendLine("```");
            sb.AppendLine(content);
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("Please produce the JSON changes object described in your system prompt.");

        return sb.ToString();
    }

    private static string ExtractTextFromResponse(string responseBody)
    {
        var node = JsonNode.Parse(responseBody);
        return node?["content"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Unexpected Anthropic response structure.");
    }

    /// <summary>
    /// Extracts the first JSON object found in the AI text, stripping any surrounding prose.
    /// </summary>
    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end < 0 || end <= start)
            return text; // return as-is and let the caller deal with the parse error

        return text[start..(end + 1)];
    }
}
