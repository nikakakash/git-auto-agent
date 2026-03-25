# GitAutoAgent

A C# .NET 8 console application that automates GitHub pull requests using AI. Give it a repository and a task description — it forks the repo, clones it, uses Claude AI to generate the code changes, commits, pushes, and opens a pull request automatically.

---

## How It Works

The agent runs a 9-step pipeline:

```
1. Authenticate       → verify GitHub token, get your username
2. Fork               → fork the target repo into your GitHub account
3. Clone              → clone your fork locally into a temp directory
4. Branch             → create a feature branch (e.g. auto/add-dark-mode)
5. Gather context     → read the repo's files to give Claude full context
6. AI generation      → send task + files to Claude, get back file changes as JSON
7. Apply changes      → write Claude's changes to the local clone
8. Commit & push      → stage all changes, commit, push branch to your fork
9. Open PR            → create a pull request from your fork → the original repo
```

The temp directory is cleaned up automatically after the run.

---

## Project Structure

```
GitAutoAgent/
├── README.md
├── GitAutoAgent.slnx                        # Solution file
└── src/
    └── GitAutoAgent/
        └── GitAutoAgent/
            ├── GitAutoAgent.csproj
            ├── appsettings.json             # API keys and configuration
            ├── Program.cs                   # Entry point, CLI argument parsing
            ├── Configuration/
            │   └── AppSettings.cs           # Strongly-typed config binding
            ├── Models/
            │   ├── TaskRequest.cs           # Input model (repo, task, base branch)
            │   ├── AgentResult.cs           # Output model (PR URL, status, logs)
            │   └── RepoContext.cs           # Runtime repo metadata and paths
            ├── Services/
            │   ├── GitHubService.cs         # GitHub API: fork, create PR (Octokit)
            │   ├── GitService.cs            # Local git ops: clone, branch, commit, push (LibGit2Sharp)
            │   ├── AiCodeService.cs         # Calls Claude API, parses JSON changes
            │   └── OrchestrationService.cs  # Coordinates all 9 pipeline steps
            └── Helpers/
                ├── FileHelper.cs            # File read/write/enumerate utilities
                └── DiffHelper.cs            # Applies AI-generated changes to files
```

---

## Tech Stack

| Package | Version | Purpose |
|---------|---------|---------|
| [Octokit](https://github.com/octokit/octokit.net) | 13.0.1 | GitHub API client — fork repos, open PRs |
| [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) | 0.30.0 | Local git operations — clone, branch, commit, push |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | Dependency injection container |
| Microsoft.Extensions.Configuration.Json | 8.0.1 | appsettings.json binding |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 8.0.0 | Override config via env vars |
| Microsoft.Extensions.Http | 8.0.1 | HttpClientFactory for Claude API calls |
| Microsoft.Extensions.Logging.Console | 8.0.1 | Console logging with ILogger |
| System.CommandLine | 2.0.0-beta4 | CLI argument parsing |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) or later
- A **GitHub Personal Access Token** with `repo` scope
- An **Anthropic API key** with available credits

### Getting a GitHub Token

1. Go to GitHub → Settings → Developer Settings → Personal Access Tokens → Tokens (classic)
2. Click **Generate new token**
3. Select the `repo` scope (full control of private repositories)
4. Copy the token — you only see it once

### Getting an Anthropic API Key

1. Go to [console.anthropic.com](https://console.anthropic.com)
2. Sign up / log in
3. Navigate to **API Keys** and create a new key
4. Add credits under **Plans & Billing** (minimum $5 is enough for many runs)

---

## Setup

### 1. Clone this repository

```bash
git clone <this-repo-url>
cd GitAutoAgent
```

### 2. Configure your API keys

Open `src/GitAutoAgent/GitAutoAgent/appsettings.json` and fill in your credentials:

```json
{
  "GitHub": {
    "Token": "ghp_your_github_token_here",
    "DefaultBaseBranch": "main"
  },
  "Anthropic": {
    "ApiKey": "sk-ant-your_anthropic_key_here",
    "Model": "claude-sonnet-4-5",
    "MaxTokens": 8096
  },
  "Agent": {
    "WorkingDirectory": "./temp",
    "MaxFilesToAnalyze": 20,
    "MaxFileSizeBytes": 50000
  }
}
```

Alternatively, set them as environment variables (these override appsettings.json):

```bash
# Windows PowerShell
$env:GitHub__Token = "ghp_..."
$env:Anthropic__ApiKey = "sk-ant-..."

# Linux / macOS
export GitHub__Token="ghp_..."
export Anthropic__ApiKey="sk-ant-..."
```

### 3. Restore dependencies

```bash
cd src/GitAutoAgent/GitAutoAgent
dotnet restore
```

---

## Running the Agent

Navigate to the project directory first:

```bash
cd src/GitAutoAgent/GitAutoAgent
```

### Basic usage

```bash
dotnet run -- --repo "owner/reponame" --task "describe what you want done"
```

### Required flags

| Flag | Description | Example |
|------|-------------|---------|
| `--repo` | GitHub repository in `owner/name` format | `octocat/hello-world` |
| `--task` | Natural-language description of the changes | `"Add a CONTRIBUTING.md"` |

### Optional flags

| Flag | Description | Default |
|------|-------------|---------|
| `--base-branch` | Target branch for the pull request | `main` |

---

## Usage Examples

```bash
# Add a contributing guide
dotnet run -- --repo "octocat/hello-world" --task "Add a CONTRIBUTING.md with sections for setup, coding standards, and PR process"

# Fix a bug
dotnet run -- --repo "yourname/api-project" --task "Fix the null reference exception in UserService.GetById method"

# Add a feature
dotnet run -- --repo "yourname/webapp" --task "Add dark mode support using CSS variables"

# Add a badge to a README
dotnet run -- --repo "octocat/hello-world" --task "Add a README badge for build status"

# Target a repo that uses 'master' instead of 'main'
dotnet run -- --repo "yourname/legacy-repo" --task "Add a .gitignore for Node.js" --base-branch "master"
```

### Tips for better AI results

- **Be specific.** Instead of `"fix the code"`, say `"Add input validation to the login form that checks email format and minimum 8-character password length"`
- **Name the files** when you know them: `"Update the README.md to include installation instructions and usage examples"`
- **Describe the outcome**, not just the action: `"Add error handling to the API so it returns a 404 JSON response instead of crashing when a user is not found"`

---

## Example Output

```
╔══════════════════════════════════════════════╗
║          GitHub PR Automation Agent          ║
╚══════════════════════════════════════════════╝
  Repo  : octocat/hello-world
  Task  : Add a CONTRIBUTING.md with setup instructions
  Base  : main

info: Authenticated as: yourUsername
info: Forking octocat/hello-world…
info: Fork created: https://github.com/yourUsername/Hello-World
info: Fork is ready.
info: Cloning fork into ./temp/hello-world-a1b2c3d4…
info: Clone complete.
info: Creating branch 'auto/add-a-contributing-md-with-setup-instructions'
info: Gathering repository context…
info: Loaded 2 file(s) as AI context.
info: Calling Claude API (claude-sonnet-4-5)…
info: AI proposed 1 change(s). Commit: "Add CONTRIBUTING.md with setup instructions"
info: Applied 1 file operation(s).
info: Committing and pushing…
info: Push complete.
info: Opening pull request…
info: PR opened: https://github.com/octocat/hello-world/pull/42

── Pipeline Log ──────────────────────────────────
  [14:40:11] Authenticated as: yourUsername
  [14:40:13] Fork ready: https://github.com/yourUsername/Hello-World.git
  [14:40:18] Clone complete.
  [14:40:20] Creating branch 'auto/add-a-contributing-md-with-setup-instructions'
  [14:40:20] Loaded 2 file(s) as AI context.
  [14:40:22] AI proposed 1 change(s).
  [14:40:22] Applied 1 file operation(s).
  [14:40:24] Push complete.
  [14:40:25] PR opened: https://github.com/octocat/hello-world/pull/42

✓  Success! PR opened: https://github.com/octocat/hello-world/pull/42
```

---

## Configuration Reference

All settings live in `appsettings.json`:

```json
{
  "GitHub": {
    "Token": "",               // Personal access token with 'repo' scope
    "DefaultBaseBranch": "main" // Fallback base branch if --base-branch not specified
  },
  "Anthropic": {
    "ApiKey": "",              // Anthropic API key
    "Model": "claude-sonnet-4-5", // Claude model to use
    "MaxTokens": 8096          // Max tokens for Claude's response
  },
  "Agent": {
    "WorkingDirectory": "./temp",  // Where to clone repos during a run
    "MaxFilesToAnalyze": 20,       // Max number of files sent to Claude as context
    "MaxFileSizeBytes": 50000      // Files larger than this are skipped
  }
}
```

---

## How the AI Integration Works

The agent sends two things to Claude:

1. **A system prompt** instructing Claude to act as a software engineer and return changes as a strict JSON object
2. **A user message** containing the task description + the full content of the repo's relevant files

Claude responds with a JSON object like this:

```json
{
  "changes": [
    {
      "filePath": "CONTRIBUTING.md",
      "action": "create",
      "content": "# Contributing\n\n## Setup\n..."
    },
    {
      "filePath": "README.md",
      "action": "modify",
      "content": "# Hello World\n\nSee [CONTRIBUTING.md](CONTRIBUTING.md)...\n"
    }
  ],
  "commitMessage": "Add CONTRIBUTING.md with setup instructions",
  "prTitle": "Docs: Add contributing guide",
  "prDescription": "## Changes\n- Added CONTRIBUTING.md\n- Updated README with link"
}
```

The agent then writes those files to the local clone, commits, and opens the PR with the AI-generated title and description.

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `401 Unauthorized` (GitHub) | Invalid or expired token | Regenerate token at GitHub → Settings → Developer Settings |
| `401 Unauthorized` (Anthropic) | Invalid API key | Check key at console.anthropic.com |
| `credit balance is too low` | No Anthropic credits | Add credits at console.anthropic.com → Plans & Billing |
| `Push failed / 403` | Token missing `repo` scope | Regenerate token and enable the `repo` scope checkbox |
| `No changes to commit` | AI changes matched existing files | Try a more specific task description |
| Temp folder not deleted | LibGit2Sharp file lock on exit | Delete `temp/` folder manually — does not affect the result |
| `Repository not found` | Repo is private or name is wrong | Check the `owner/name` format matches the GitHub URL |

---

## Architecture Decisions

**Why fork instead of pushing directly?**
Forking is the standard open-source contribution workflow. It means the agent never needs write access to the upstream repo — only a GitHub token is required, and the repo owner reviews the PR before merging.

**Why LibGit2Sharp instead of shelling out to `git`?**
LibGit2Sharp is a pure C# binding to libgit2. It works identically on Windows, macOS, and Linux without requiring git to be installed on the machine.

**Why structured JSON from Claude?**
Asking Claude to return a strict JSON schema (file path, action, content) makes the response machine-parseable and deterministic. Prose-based diffs are fragile — the JSON approach lets the agent apply changes reliably regardless of file type.

---

## License

MIT
