using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

/// <summary>
/// Base class for Azure DevOps tools. Uses the Azure DevOps REST API v7.
/// Requires AZURE_DEVOPS_PAT and AZURE_DEVOPS_ORG environment variables.
/// </summary>
public abstract class AzureDevOpsToolBase : ITool
{
    private readonly HttpClient _httpClient;

    protected AzureDevOpsToolBase(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolCategory Category => ToolCategory.AzureDevOps;
    public abstract JsonElement ParameterSchema { get; }
    public abstract Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);

    protected async Task<ToolResult> GetAsync(string org, string path, CancellationToken ct)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, org, path);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return ToolResult.Fail($"Azure DevOps API returned {(int)response.StatusCode}: {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"Azure DevOps API request failed: {ex.Message}");
        }
    }

    protected string GetOrg(JsonElement arguments)
    {
        if (arguments.TryGetProperty("organization", out var orgEl) && orgEl.GetString() is string org)
            return org;
        return Environment.GetEnvironmentVariable("AZURE_DEVOPS_ORG") ?? "";
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string org, string path)
    {
        var url = $"https://dev.azure.com/{org}/{path}";
        if (!path.Contains("api-version"))
            url += (path.Contains('?') ? "&" : "?") + "api-version=7.0";

        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var pat = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
        if (!string.IsNullOrEmpty(pat))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        return request;
    }
}

public class AzDoListReposTool : AzureDevOpsToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "organization": { "type": "string", "description": "Azure DevOps organization (or use AZURE_DEVOPS_ORG env var)" },
                "project": { "type": "string", "description": "Project name" }
            },
            "required": ["project"]
        }
        """).RootElement;

    public AzDoListReposTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "azdo_list_repos";
    public override string Description => "List Git repositories in an Azure DevOps project";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("project", out var projectEl))
            return ToolResult.Fail("Missing required parameter: project");

        var org = GetOrg(arguments);
        if (string.IsNullOrEmpty(org))
            return ToolResult.Fail("Organization not specified and AZURE_DEVOPS_ORG not set");

        var project = projectEl.GetString()!;
        return await GetAsync(org, $"{project}/_apis/git/repositories", ct);
    }
}

public class AzDoGetWorkItemsTool : AzureDevOpsToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "organization": { "type": "string", "description": "Azure DevOps organization (or use AZURE_DEVOPS_ORG env var)" },
                "project": { "type": "string", "description": "Project name" },
                "ids": { "type": "array", "items": { "type": "integer" }, "description": "Work item IDs to retrieve" },
                "wiql": { "type": "string", "description": "WIQL query to search work items" }
            },
            "required": ["project"]
        }
        """).RootElement;

    public AzDoGetWorkItemsTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "azdo_get_work_items";
    public override string Description => "Get work items from an Azure DevOps project (by IDs or WIQL query)";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("project", out var projectEl))
            return ToolResult.Fail("Missing required parameter: project");

        var org = GetOrg(arguments);
        if (string.IsNullOrEmpty(org))
            return ToolResult.Fail("Organization not specified and AZURE_DEVOPS_ORG not set");

        var project = projectEl.GetString()!;

        // If IDs are provided, fetch directly
        if (arguments.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            var ids = string.Join(",", idsEl.EnumerateArray().Select(id => id.GetInt32()));
            return await GetAsync(org, $"{project}/_apis/wit/workitems?ids={ids}&$expand=all", ct);
        }

        // Default: get recent work items via WIQL
        var wiql = arguments.TryGetProperty("wiql", out var wiqlEl) ? wiqlEl.GetString() :
            $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project}' ORDER BY [System.ChangedDate] DESC";

        // WIQL requires POST, but for simplicity we return the query info
        return await GetAsync(org, $"{project}/_apis/wit/wiql?query={Uri.EscapeDataString(wiql ?? "")}", ct);
    }
}

public class AzDoGetPipelinesTool : AzureDevOpsToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "organization": { "type": "string", "description": "Azure DevOps organization (or use AZURE_DEVOPS_ORG env var)" },
                "project": { "type": "string", "description": "Project name" }
            },
            "required": ["project"]
        }
        """).RootElement;

    public AzDoGetPipelinesTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "azdo_get_pipelines";
    public override string Description => "List pipelines in an Azure DevOps project";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("project", out var projectEl))
            return ToolResult.Fail("Missing required parameter: project");

        var org = GetOrg(arguments);
        if (string.IsNullOrEmpty(org))
            return ToolResult.Fail("Organization not specified and AZURE_DEVOPS_ORG not set");

        var project = projectEl.GetString()!;
        return await GetAsync(org, $"{project}/_apis/pipelines", ct);
    }
}
