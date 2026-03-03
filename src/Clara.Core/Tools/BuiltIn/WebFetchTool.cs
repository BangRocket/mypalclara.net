using System.Text.Json;

namespace Clara.Core.Tools.BuiltIn;

public class WebFetchTool : ITool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "URL to fetch" }
            },
            "required": ["url"]
        }
        """).RootElement;

    private readonly HttpClient _httpClient;

    public WebFetchTool(HttpClient httpClient) => _httpClient = httpClient;

    public string Name => "web_fetch";
    public string Description => "Fetch content from a URL";
    public ToolCategory Category => ToolCategory.Web;
    public JsonElement ParameterSchema => Schema;

    public async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("url", out var urlEl))
            return ToolResult.Fail("Missing required parameter: url");

        var url = urlEl.GetString();
        if (string.IsNullOrWhiteSpace(url))
            return ToolResult.Fail("URL cannot be empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return ToolResult.Fail($"Invalid URL: {url}");

        try
        {
            var response = await _httpClient.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);

            // Truncate very large responses
            const int maxLength = 50_000;
            if (content.Length > maxLength)
                content = content[..maxLength] + "\n\n[... truncated]";

            return ToolResult.Ok(content);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Fetch failed: {ex.Message}");
        }
    }
}
