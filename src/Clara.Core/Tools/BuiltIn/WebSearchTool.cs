using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

public class WebSearchTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": { "type": "string", "description": "Search query" }
            },
            "required": ["query"]
        }
        """).RootElement;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public WebSearchTool(HttpClient httpClient, string? tavilyApiKey = null)
    {
        _httpClient = httpClient;
        _apiKey = tavilyApiKey ?? Environment.GetEnvironmentVariable("TAVILY_API_KEY");
    }

    public string Name => "web_search";
    public string Description => "Search the web using Tavily and return results";
    public ToolCategory Category => ToolCategory.Web;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("query", out var queryEl))
            return ToolResult.Fail("Missing required parameter: query");

        var query = queryEl.GetString();
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Fail("Query cannot be empty");

        if (string.IsNullOrWhiteSpace(_apiKey))
            return ToolResult.Fail("TAVILY_API_KEY not configured");

        try
        {
            var body = JsonSerializer.Serialize(new { api_key = _apiKey, query, max_results = 5 });
            var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.tavily.com/search", content, ct);
            response.EnsureSuccessStatusCode();

            var resultJson = await response.Content.ReadAsStringAsync(ct);
            return ToolResult.Ok(resultJson);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Search failed: {ex.Message}");
        }
    }
}
