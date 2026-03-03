using System.Net;
using System.Text.Json;
using Clara.Core.Tools;
using Clara.Core.Tools.BuiltIn;

namespace Clara.Core.Tests.Tools.BuiltIn;

public class AzureDevOpsToolsTests
{
    private static readonly ToolExecutionContext TestContext = new("user1", "session1", "test", false, null);

    [Fact]
    public void ListRepos_has_correct_metadata()
    {
        var tool = new AzDoListReposTool(new HttpClient());

        Assert.Equal("azdo_list_repos", tool.Name);
        Assert.Equal(ToolCategory.AzureDevOps, tool.Category);
    }

    [Fact]
    public void ListRepos_schema_requires_project()
    {
        var tool = new AzDoListReposTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.True(schema.TryGetProperty("required", out var required));
        var requiredList = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("project", requiredList);
    }

    [Fact]
    public async Task ListRepos_fails_without_project()
    {
        var tool = new AzDoListReposTool(new HttpClient());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameter", result.Error);
    }

    [Fact]
    public void GetWorkItems_has_correct_metadata()
    {
        var tool = new AzDoGetWorkItemsTool(new HttpClient());

        Assert.Equal("azdo_get_work_items", tool.Name);
        Assert.Equal(ToolCategory.AzureDevOps, tool.Category);
    }

    [Fact]
    public void GetPipelines_has_correct_metadata()
    {
        var tool = new AzDoGetPipelinesTool(new HttpClient());

        Assert.Equal("azdo_get_pipelines", tool.Name);
        Assert.Equal(ToolCategory.AzureDevOps, tool.Category);
    }

    [Fact]
    public void All_azdo_tools_have_azure_devops_category()
    {
        var httpClient = new HttpClient();
        ITool[] tools =
        [
            new AzDoListReposTool(httpClient),
            new AzDoGetWorkItemsTool(httpClient),
            new AzDoGetPipelinesTool(httpClient),
        ];

        Assert.All(tools, t => Assert.Equal(ToolCategory.AzureDevOps, t.Category));
    }
}

public class GoogleWorkspaceToolsTests
{
    private static readonly ToolExecutionContext TestContext = new("user1", "session1", "test", false, null);

    [Fact]
    public void ListFiles_has_correct_metadata()
    {
        var tool = new GoogleListFilesTool(new HttpClient());

        Assert.Equal("google_list_files", tool.Name);
        Assert.Equal(ToolCategory.GoogleWorkspace, tool.Category);
    }

    [Fact]
    public void ReadSheet_has_correct_metadata()
    {
        var tool = new GoogleReadSheetTool(new HttpClient());

        Assert.Equal("google_read_sheet", tool.Name);
        Assert.Equal(ToolCategory.GoogleWorkspace, tool.Category);
    }

    [Fact]
    public void ReadSheet_schema_requires_spreadsheet_id()
    {
        var tool = new GoogleReadSheetTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.True(schema.TryGetProperty("required", out var required));
        var requiredList = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("spreadsheet_id", requiredList);
    }

    [Fact]
    public async Task ReadSheet_fails_without_spreadsheet_id()
    {
        var tool = new GoogleReadSheetTool(new HttpClient());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameter", result.Error);
    }

    [Fact]
    public void CreateDoc_has_correct_metadata()
    {
        var tool = new GoogleCreateDocTool(new HttpClient());

        Assert.Equal("google_create_doc", tool.Name);
        Assert.Equal(ToolCategory.GoogleWorkspace, tool.Category);
    }

    [Fact]
    public void CreateDoc_schema_requires_title()
    {
        var tool = new GoogleCreateDocTool(new HttpClient());
        var schema = tool.ParameterSchema;

        Assert.True(schema.TryGetProperty("required", out var required));
        var requiredList = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("title", requiredList);
    }

    [Fact]
    public async Task CreateDoc_fails_without_title()
    {
        var tool = new GoogleCreateDocTool(new HttpClient());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
    }

    [Fact]
    public void All_google_tools_have_google_workspace_category()
    {
        var httpClient = new HttpClient();
        ITool[] tools =
        [
            new GoogleListFilesTool(httpClient),
            new GoogleReadSheetTool(httpClient),
            new GoogleCreateDocTool(httpClient),
        ];

        Assert.All(tools, t => Assert.Equal(ToolCategory.GoogleWorkspace, t.Category));
    }
}

public class EmailToolsTests
{
    private static readonly ToolExecutionContext TestContext = new("user1", "session1", "test", false, null);

    [Fact]
    public void EmailCheck_has_correct_metadata()
    {
        var tool = new EmailCheckTool(new StubEmailService());

        Assert.Equal("email_check", tool.Name);
        Assert.Equal(ToolCategory.Email, tool.Category);
    }

    [Fact]
    public async Task EmailCheck_returns_no_emails_with_stub_service()
    {
        var tool = new EmailCheckTool(new StubEmailService());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("No emails found", result.Content);
    }

    [Fact]
    public async Task EmailCheck_returns_email_list()
    {
        var emails = new List<EmailSummary>
        {
            new("msg1", "alice@example.com", "Hello", DateTime.UtcNow, false),
            new("msg2", "bob@example.com", "Meeting", DateTime.UtcNow, true),
        };
        var service = new FakeEmailService { InboxEmails = emails };
        var tool = new EmailCheckTool(service);
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("2 email(s)", result.Content);
        Assert.Contains("alice@example.com", result.Content);
        Assert.Contains("bob@example.com", result.Content);
    }

    [Fact]
    public void EmailRead_has_correct_metadata()
    {
        var tool = new EmailReadTool(new StubEmailService());

        Assert.Equal("email_read", tool.Name);
        Assert.Equal(ToolCategory.Email, tool.Category);
    }

    [Fact]
    public async Task EmailRead_fails_without_message_id()
    {
        var tool = new EmailReadTool(new StubEmailService());
        var args = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameter", result.Error);
    }

    [Fact]
    public async Task EmailRead_returns_not_found_for_missing_message()
    {
        var tool = new EmailReadTool(new StubEmailService());
        var args = JsonDocument.Parse("""{"message_id":"nonexistent"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmailRead_returns_full_email()
    {
        var email = new EmailMessage("msg1", "alice@example.com", "me@example.com", "Test Subject", "Hello World", DateTime.UtcNow, ["attachment.pdf"]);
        var service = new FakeEmailService { StoredEmail = email };
        var tool = new EmailReadTool(service);
        var args = JsonDocument.Parse("""{"message_id":"msg1"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("alice@example.com", result.Content);
        Assert.Contains("Test Subject", result.Content);
        Assert.Contains("Hello World", result.Content);
        Assert.Contains("attachment.pdf", result.Content);
    }

    [Fact]
    public void EmailSendAlert_has_correct_metadata()
    {
        var tool = new EmailSendAlertTool(new StubEmailService());

        Assert.Equal("email_send_alert", tool.Name);
        Assert.Equal(ToolCategory.Email, tool.Category);
    }

    [Fact]
    public async Task EmailSendAlert_fails_without_required_params()
    {
        var tool = new EmailSendAlertTool(new StubEmailService());
        var args = JsonDocument.Parse("""{"to":"x","subject":"y"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameters", result.Error);
    }

    [Fact]
    public async Task EmailSendAlert_returns_fail_with_stub_service()
    {
        var tool = new EmailSendAlertTool(new StubEmailService());
        var args = JsonDocument.Parse("""{"to":"x@y.com","subject":"Alert","body":"Test"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.False(result.Success); // Stub returns false
    }

    [Fact]
    public async Task EmailSendAlert_returns_ok_when_sent()
    {
        var service = new FakeEmailService { SendResult = true };
        var tool = new EmailSendAlertTool(service);
        var args = JsonDocument.Parse("""{"to":"x@y.com","subject":"Alert","body":"Test"}""").RootElement;

        var result = await tool.ExecuteAsync(args, TestContext);

        Assert.True(result.Success);
        Assert.Contains("Alert sent", result.Content);
    }

    [Fact]
    public void All_email_tools_have_email_category()
    {
        var service = new StubEmailService();
        ITool[] tools =
        [
            new EmailCheckTool(service),
            new EmailReadTool(service),
            new EmailSendAlertTool(service),
        ];

        Assert.All(tools, t => Assert.Equal(ToolCategory.Email, t.Category));
    }

    /// <summary>
    /// Fake email service for testing.
    /// </summary>
    private class FakeEmailService : IEmailService
    {
        public List<EmailSummary> InboxEmails { get; set; } = [];
        public EmailMessage? StoredEmail { get; set; }
        public bool SendResult { get; set; }

        public Task<IReadOnlyList<EmailSummary>> CheckInboxAsync(string? folder, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmailSummary>>(InboxEmails.Take(limit).ToList());

        public Task<EmailMessage?> ReadEmailAsync(string messageId, CancellationToken ct)
            => Task.FromResult(StoredEmail);

        public Task<bool> SendAlertAsync(string to, string subject, string body, CancellationToken ct)
            => Task.FromResult(SendResult);
    }
}
