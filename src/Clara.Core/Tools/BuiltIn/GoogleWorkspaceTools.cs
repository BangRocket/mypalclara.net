using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

/// <summary>
/// Base class for Google Workspace tools. Uses the Google APIs.
/// Requires GOOGLE_ACCESS_TOKEN environment variable or OAuth token from McpOAuthHandler.
/// </summary>
public abstract class GoogleWorkspaceToolBase : ITool
{
    private readonly HttpClient _httpClient;

    protected GoogleWorkspaceToolBase(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolCategory Category => ToolCategory.GoogleWorkspace;
    public abstract JsonElement ParameterSchema { get; }
    public abstract Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);

    protected async Task<ToolResult> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuth(request);
            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return ToolResult.Fail($"Google API returned {(int)response.StatusCode}: {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"Google API request failed: {ex.Message}");
        }
    }

    protected async Task<ToolResult> PostAsync(string url, object payload, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAuth(request);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return ToolResult.Fail($"Google API returned {(int)response.StatusCode}: {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Fail($"Google API request failed: {ex.Message}");
        }
    }

    private static void AddAuth(HttpRequestMessage request)
    {
        var token = Environment.GetEnvironmentVariable("GOOGLE_ACCESS_TOKEN");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public class GoogleListFilesTool : GoogleWorkspaceToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": { "type": "string", "description": "Search query (Drive search syntax, e.g. \"name contains 'report'\")" },
                "page_size": { "type": "integer", "description": "Maximum results (default 20)", "default": 20 },
                "mime_type": { "type": "string", "description": "Filter by MIME type (e.g. application/vnd.google-apps.spreadsheet)" }
            }
        }
        """).RootElement;

    public GoogleListFilesTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "google_list_files";
    public override string Description => "List files from Google Drive";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var pageSize = arguments.TryGetProperty("page_size", out var psEl) ? psEl.GetInt32() : 20;
        var queryParts = new List<string>();

        if (arguments.TryGetProperty("query", out var queryEl) && queryEl.GetString() is string q)
            queryParts.Add(q);
        if (arguments.TryGetProperty("mime_type", out var mimeEl) && mimeEl.GetString() is string mime)
            queryParts.Add($"mimeType='{mime}'");

        var queryParam = queryParts.Count > 0 ? $"&q={Uri.EscapeDataString(string.Join(" and ", queryParts))}" : "";

        return await GetAsync(
            $"https://www.googleapis.com/drive/v3/files?pageSize={pageSize}&fields=files(id,name,mimeType,modifiedTime,size){queryParam}",
            ct);
    }
}

public class GoogleReadSheetTool : GoogleWorkspaceToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "spreadsheet_id": { "type": "string", "description": "Google Sheets spreadsheet ID" },
                "range": { "type": "string", "description": "A1 notation range (e.g. 'Sheet1!A1:D10')", "default": "Sheet1" }
            },
            "required": ["spreadsheet_id"]
        }
        """).RootElement;

    public GoogleReadSheetTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "google_read_sheet";
    public override string Description => "Read data from a Google Sheets spreadsheet";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("spreadsheet_id", out var idEl))
            return ToolResult.Fail("Missing required parameter: spreadsheet_id");

        var spreadsheetId = idEl.GetString()!;
        var range = arguments.TryGetProperty("range", out var rangeEl) ? rangeEl.GetString() ?? "Sheet1" : "Sheet1";

        return await GetAsync(
            $"https://sheets.googleapis.com/v4/spreadsheets/{spreadsheetId}/values/{Uri.EscapeDataString(range)}",
            ct);
    }
}

public class GoogleCreateDocTool : GoogleWorkspaceToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "title": { "type": "string", "description": "Document title" },
                "content": { "type": "string", "description": "Initial document content (plain text)" }
            },
            "required": ["title"]
        }
        """).RootElement;

    public GoogleCreateDocTool(HttpClient httpClient) : base(httpClient) { }

    public override string Name => "google_create_doc";
    public override string Description => "Create a new Google Docs document";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("title", out var titleEl))
            return ToolResult.Fail("Missing required parameter: title");

        var title = titleEl.GetString()!;
        var payload = new { title };

        return await PostAsync("https://docs.googleapis.com/v1/documents", payload, ct);
    }
}
