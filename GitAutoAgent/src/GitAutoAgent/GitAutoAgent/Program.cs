using System.CommandLine;
using GitAutoAgent.Configuration;
using GitAutoAgent.Models;
using GitAutoAgent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── CLI argument definitions ─────────────────────────────────────────────────
var repoOption = new Option<string>(
    name: "--repo",
    description: "GitHub repository in 'owner/name' format (e.g. octocat/hello-world)")
{
    IsRequired = true
};

var taskOption = new Option<string>(
    name: "--task",
    description: "Natural-language description of the changes to make")
{
    IsRequired = true
};

var branchOption = new Option<string>(
    name: "--base-branch",
    description: "Base branch to target with the pull request",
    getDefaultValue: () => "main");

var rootCommand = new RootCommand("GitHub PR Automation Agent — forks, applies AI changes, and opens a PR")
{
    repoOption,
    taskOption,
    branchOption
};

rootCommand.SetHandler(async (string repo, string task, string baseBranch) =>
{
    // ── validate repo format ─────────────────────────────────────────────────
    if (!repo.Contains('/') || repo.Split('/').Length != 2)
    {
        Console.Error.WriteLine("ERROR: --repo must be in 'owner/name' format.");
        Environment.Exit(1);
    }

    // ── build DI container ───────────────────────────────────────────────────
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    var appSettings = config.Get<AppSettings>() ?? new AppSettings();

    // Basic validation
    if (string.IsNullOrWhiteSpace(appSettings.GitHub.Token))
    {
        Console.Error.WriteLine("ERROR: GitHub:Token is not set in appsettings.json or environment.");
        Environment.Exit(1);
    }
    if (string.IsNullOrWhiteSpace(appSettings.Anthropic.ApiKey))
    {
        Console.Error.WriteLine("ERROR: Anthropic:ApiKey is not set in appsettings.json or environment.");
        Environment.Exit(1);
    }

    var services = new ServiceCollection();

    services.AddSingleton(appSettings);

    services.AddHttpClient("Anthropic");

    services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    });

    services.AddSingleton<GitHubService>();
    services.AddSingleton<GitService>();
    services.AddSingleton<AiCodeService>();
    services.AddSingleton<OrchestrationService>();

    await using var provider = services.BuildServiceProvider();

    // ── run the pipeline ─────────────────────────────────────────────────────
    var orchestrator = provider.GetRequiredService<OrchestrationService>();

    var request = new TaskRequest
    {
        Repository      = repo,
        TaskDescription = task,
        BaseBranch      = baseBranch
    };

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════╗");
    Console.WriteLine("║          GitHub PR Automation Agent          ║");
    Console.WriteLine("╚══════════════════════════════════════════════╝");
    Console.WriteLine($"  Repo  : {repo}");
    Console.WriteLine($"  Task  : {task}");
    Console.WriteLine($"  Base  : {baseBranch}");
    Console.WriteLine();

    var result = await orchestrator.RunAsync(request);

    Console.WriteLine();
    Console.WriteLine("── Pipeline Log ──────────────────────────────────");
    foreach (var log in result.StepLogs)
        Console.WriteLine($"  {log}");
    Console.WriteLine();

    if (result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓  {result.Message}");
        Console.ResetColor();
        Environment.Exit(0);
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗  {result.Message}");
        if (result.ErrorDetails is not null)
        {
            Console.WriteLine();
            Console.WriteLine("── Error Details ─────────────────────────────────");
            Console.WriteLine(result.ErrorDetails);
        }
        Console.ResetColor();
        Environment.Exit(1);
    }

}, repoOption, taskOption, branchOption);

return await rootCommand.InvokeAsync(args);
