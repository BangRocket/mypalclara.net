using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

/// <summary>
/// Base class for GitHub API tools. Uses the GitHub REST API v3.
/// Requires a GITHUB_TOKEN environment variable or configured token.
/// </summary>
public abstract class GitHubToolBase : ITool
{
    private const string ApiBase = "https://api.github.com";
    private readonly HttpClient _httpClient;

    protected GitHubToolBase(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolCategory Category => ToolCategory.GitHub;
    public abstract JsonElement ParameterSchema { get; }
    public abstract Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);

    protected async Task<ToolResult> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, path);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return ToolResult.Fail($"GitHub API returned {(int)response.StatusCode}: {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"GitHub API request failed: {ex.Message}");
        }
    }

    protected async Task<ToolResult> PostAsync(string path, object payload, CancellationToken ct)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Post, path);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return ToolResult.Fail($"GitHub API returned {(int)response.StatusCode}: {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"GitHub API request failed: {ex.Message}");
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : $"{ApiBase}{path}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Clara", "1.0"));
        return request;
    }
}

public class GitHubListReposTool : GitHubToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "sort": { "type": "string", "description": "Sort by: created, updated, pushed, full_name", "default": "updated" },
                "per_page": { "type": "integer", "description": "Results per page (max 100)", "default": 30 }
            }
        }
        """).RootElement;

    public GitHubListReposTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "github_list_repos";
    public override string Description => "List repositories for the authenticated GitHub user";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var sort = arguments.TryGetProperty("sort", out var sortEl) ? sortEl.GetString() ?? "updated" : "updated";
        var perPage = arguments.TryGetProperty("per_page", out var ppEl) ? ppEl.GetInt32() : 30;

        return await GetAsync($"/user/repos?sort={sort}&per_page={perPage}", ct);
    }
}

public class GitHubGetIssuesTool : GitHubToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo": { "type": "string", "description": "Repository name" },
                "state": { "type": "string", "description": "Issue state: open, closed, all", "default": "open" },
                "per_page": { "type": "integer", "description": "Results per page (max 100)", "default": 30 }
            },
            "required": ["owner", "repo"]
        }
        """).RootElement;

    public GitHubGetIssuesTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "github_get_issues";
    public override string Description => "Get issues for a GitHub repository";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("owner", out var ownerEl) || !arguments.TryGetProperty("repo", out var repoEl))
            return ToolResult.Fail("Missing required parameters: owner, repo");

        var owner = ownerEl.GetString()!;
        var repo = repoEl.GetString()!;
        var state = arguments.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "open" : "open";
        var perPage = arguments.TryGetProperty("per_page", out var ppEl) ? ppEl.GetInt32() : 30;

        return await GetAsync($"/repos/{owner}/{repo}/issues?state={state}&per_page={perPage}", ct);
    }
}

public class GitHubGetPullRequestsTool : GitHubToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo": { "type": "string", "description": "Repository name" },
                "state": { "type": "string", "description": "PR state: open, closed, all", "default": "open" },
                "per_page": { "type": "integer", "description": "Results per page (max 100)", "default": 30 }
            },
            "required": ["owner", "repo"]
        }
        """).RootElement;

    public GitHubGetPullRequestsTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "github_get_pull_requests";
    public override string Description => "Get pull requests for a GitHub repository";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("owner", out var ownerEl) || !arguments.TryGetProperty("repo", out var repoEl))
            return ToolResult.Fail("Missing required parameters: owner, repo");

        var owner = ownerEl.GetString()!;
        var repo = repoEl.GetString()!;
        var state = arguments.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "open" : "open";
        var perPage = arguments.TryGetProperty("per_page", out var ppEl) ? ppEl.GetInt32() : 30;

        return await GetAsync($"/repos/{owner}/{repo}/pulls?state={state}&per_page={perPage}", ct);
    }
}

public class GitHubCreateIssueTool : GitHubToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo": { "type": "string", "description": "Repository name" },
                "title": { "type": "string", "description": "Issue title" },
                "body": { "type": "string", "description": "Issue body (markdown)" },
                "labels": { "type": "array", "items": { "type": "string" }, "description": "Labels to add" }
            },
            "required": ["owner", "repo", "title"]
        }
        """).RootElement;

    public GitHubCreateIssueTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "github_create_issue";
    public override string Description => "Create a new issue in a GitHub repository";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("owner", out var ownerEl) ||
            !arguments.TryGetProperty("repo", out var repoEl) ||
            !arguments.TryGetProperty("title", out var titleEl))
            return ToolResult.Fail("Missing required parameters: owner, repo, title");

        var owner = ownerEl.GetString()!;
        var repo = repoEl.GetString()!;
        var payload = new Dictionary<string, object>
        {
            ["title"] = titleEl.GetString()!
        };

        if (arguments.TryGetProperty("body", out var bodyEl) && bodyEl.GetString() is string body)
            payload["body"] = body;

        if (arguments.TryGetProperty("labels", out var labelsEl) && labelsEl.ValueKind == JsonValueKind.Array)
        {
            var labels = labelsEl.EnumerateArray().Select(l => l.GetString()!).ToList();
            payload["labels"] = labels;
        }

        return await PostAsync($"/repos/{owner}/{repo}/issues", payload, ct);
    }
}

public class GitHubGetFileTool : GitHubToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": { "type": "string", "description": "Repository owner" },
                "repo": { "type": "string", "description": "Repository name" },
                "path": { "type": "string", "description": "File path in the repository" },
                "ref": { "type": "string", "description": "Branch, tag, or commit SHA (default: default branch)" }
            },
            "required": ["owner", "repo", "path"]
        }
        """).RootElement;

    public GitHubGetFileTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "github_get_file";
    public override string Description => "Get file content from a GitHub repository";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("owner", out var ownerEl) ||
            !arguments.TryGetProperty("repo", out var repoEl) ||
            !arguments.TryGetProperty("path", out var pathEl))
            return ToolResult.Fail("Missing required parameters: owner, repo, path");

        var owner = ownerEl.GetString()!;
        var repo = repoEl.GetString()!;
        var path = pathEl.GetString()!;
        var refParam = arguments.TryGetProperty("ref", out var refEl) ? $"?ref={refEl.GetString()}" : "";

        return await GetAsync($"/repos/{owner}/{repo}/contents/{path}{refParam}", ct);
    }
}
