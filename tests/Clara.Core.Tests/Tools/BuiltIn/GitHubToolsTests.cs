using System.Net;
using System.Text.Json;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;

namespace Clara.Core.Tests.Tools.BuiltIn;

public class GitHubToolsTests
{
    private static readonly ToolExecutionContext TestContext = new("user1", "session1", "test", false, null);

    private static HttpClient CreateMockClient(HttpStatusCode statusCode, string responseBody)
    {
        var handler = new MockHttpHandler(statusCode, responseBody);
        return new HttpClient(handler);
    }

    // --- GitHubListReposTool ---

    [Fact]
    public void ListRepos_has_correct_metadata()
    {
        var tool = new GitHubListReposTool(new HttpClient());

        Assert.Equal("github_list_repos", tool.Name);
        Assert.Equal(ToolCategory.GitHub, tool.Category);
        Assert.Contains("repositories", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListRepos_schema_has_optional_sort_and_per_page()
    {
        var tool = new GitHubListReposTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("sort", out _));
        Assert.True(props.TryGetProperty("per_page", out _));

        // sort and per_page are optional — no "required" array needed
    }

    [Fact]
    public async Task ListRepos_returns_ok_on_success()
    {
        var responseJson = """[{"id":1,"name":"repo1"},{"id":2,"name":"repo2"}]""";
        var client = CreateMockClient(HttpStatusCode.OK, responseJson);
        var tool = new GitHubListReposTool(client);
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("repo1", result.Content);
    }

    [Fact]
    public async Task ListRepos_returns_fail_on_error()
    {
        var client = CreateMockClient(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        var tool = new GitHubListReposTool(client);
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
    }

    // --- GitHubGetIssuesTool ---

    [Fact]
    public void GetIssues_has_correct_metadata()
    {
        var tool = new GitHubGetIssuesTool(new HttpClient());

        Assert.Equal("github_get_issues", tool.Name);
        Assert.Equal(ToolCategory.GitHub, tool.Category);
    }

    [Fact]
    public void GetIssues_schema_requires_owner_and_repo()
    {
        var tool = new GitHubGetIssuesTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.True(schema.TryGetProperty("required", out var required));
        var requiredList = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("owner", requiredList);
        Assert.Contains("repo", requiredList);
    }

    [Fact]
    public async Task GetIssues_fails_without_required_params()
    {
        var tool = new GitHubGetIssuesTool(new HttpClient());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameters", result.Error);
    }

    [Fact]
    public async Task GetIssues_returns_ok_with_valid_params()
    {
        var client = CreateMockClient(HttpStatusCode.OK, """[{"id":1,"title":"Bug"}]""");
        var tool = new GitHubGetIssuesTool(client);
        var args = JsonDocument.Parse("""{"owner":"octocat","repo":"hello-world"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("Bug", result.Content);
    }

    // --- GitHubGetPullRequestsTool ---

    [Fact]
    public void GetPRs_has_correct_metadata()
    {
        var tool = new GitHubGetPullRequestsTool(new HttpClient());

        Assert.Equal("github_get_pull_requests", tool.Name);
    }

    [Fact]
    public async Task GetPRs_fails_without_required_params()
    {
        var tool = new GitHubGetPullRequestsTool(new HttpClient());
        var args = JsonDocument.Parse("""{"owner":"x"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
    }

    // --- GitHubCreateIssueTool ---

    [Fact]
    public void CreateIssue_has_correct_metadata()
    {
        var tool = new GitHubCreateIssueTool(new HttpClient());

        Assert.Equal("github_create_issue", tool.Name);
    }

    [Fact]
    public void CreateIssue_schema_requires_owner_repo_title()
    {
        var tool = new GitHubCreateIssueTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.True(schema.TryGetProperty("required", out var required));
        var requiredList = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("owner", requiredList);
        Assert.Contains("repo", requiredList);
        Assert.Contains("title", requiredList);
    }

    [Fact]
    public async Task CreateIssue_fails_without_required_params()
    {
        var tool = new GitHubCreateIssueTool(new HttpClient());
        var args = JsonDocument.Parse("""{"owner":"x","repo":"y"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task CreateIssue_returns_ok_on_success()
    {
        var client = CreateMockClient(HttpStatusCode.Created, """{"id":42,"number":1,"title":"Test"}""");
        var tool = new GitHubCreateIssueTool(client);
        var args = JsonDocument.Parse("""{"owner":"octocat","repo":"hello","title":"Test Issue","body":"Description","labels":["bug"]}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("Test", result.Content);
    }

    // --- GitHubGetFileTool ---

    [Fact]
    public void GetFile_has_correct_metadata()
    {
        var tool = new GitHubGetFileTool(new HttpClient());

        Assert.Equal("github_get_file", tool.Name);
    }

    [Fact]
    public async Task GetFile_fails_without_required_params()
    {
        var tool = new GitHubGetFileTool(new HttpClient());
        var args = JsonDocument.Parse("""{"owner":"x"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task GetFile_returns_ok_with_valid_params()
    {
        var client = CreateMockClient(HttpStatusCode.OK, """{"name":"README.md","content":"SGVsbG8=","encoding":"base64"}""");
        var tool = new GitHubGetFileTool(client);
        var args = JsonDocument.Parse("""{"owner":"octocat","repo":"hello","path":"README.md"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("README.md", result.Content);
    }

    // --- All tools have consistent category ---

    [Fact]
    public void All_github_tools_have_github_category()
    {
        var httpClient = new HttpClient();
        ITool[] tools =
        [
            new GitHubListReposTool(httpClient),
            new GitHubGetIssuesTool(httpClient),
            new GitHubGetPullRequestsTool(httpClient),
            new GitHubCreateIssueTool(httpClient),
            new GitHubGetFileTool(httpClient),
        ];

        Assert.All(tools, t => Assert.Equal(ToolCategory.GitHub, t.Category));
    }

    // --- Mock HTTP Handler ---

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public MockHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
