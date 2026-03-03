using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clara.Core.Tools.BuiltIn;

/// <summary>
/// Base class for email tools. Uses the configured email provider (IMAP/SMTP or API).
/// This is a structured implementation that delegates to an email service.
/// For actual email operations, the gateway provides the backing service;
/// these tools define the interface and parameter schemas.
/// </summary>
public abstract class EmailToolBase : ITool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolCategory Category => ToolCategory.Email;
    public abstract JsonElement ParameterSchema { get; }
    public abstract Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default);
}

/// <summary>
/// Interface for email service backend. Implemented by the gateway module.
/// </summary>
public interface IEmailService
{
    Task<IReadOnlyList<EmailSummary>> CheckInboxAsync(string? folder, int limit, CancellationToken ct = default);
    Task<EmailMessage?> ReadEmailAsync(string messageId, CancellationToken ct = default);
    Task<bool> SendAlertAsync(string to, string subject, string body, CancellationToken ct = default);
}

public record EmailSummary(string MessageId, string From, string Subject, DateTime Date, bool IsRead);
public record EmailMessage(string MessageId, string From, string To, string Subject, string Body, DateTime Date, IReadOnlyList<string> Attachments);

/// <summary>
/// Stub email service for when no real provider is configured.
/// Returns informative messages rather than failing silently.
/// </summary>
public class StubEmailService : IEmailService
{
    public Task<IReadOnlyList<EmailSummary>> CheckInboxAsync(string? folder, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<EmailSummary>>(Array.Empty<EmailSummary>());

    public Task<EmailMessage?> ReadEmailAsync(string messageId, CancellationToken ct)
        => Task.FromResult<EmailMessage?>(null);

    public Task<bool> SendAlertAsync(string to, string subject, string body, CancellationToken ct)
        => Task.FromResult(false);
}

public class EmailCheckTool : EmailToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "folder": { "type": "string", "description": "Email folder to check (default: INBOX)", "default": "INBOX" },
                "limit": { "type": "integer", "description": "Maximum number of emails to return", "default": 10 }
            }
        }
        """).RootElement;

    private readonly IEmailService _emailService;

    public EmailCheckTool(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public override string Name => "email_check";
    public override string Description => "Check for recent emails in the inbox";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        var folder = arguments.TryGetProperty("folder", out var folderEl) ? folderEl.GetString() : "INBOX";
        var limit = arguments.TryGetProperty("limit", out var limitEl) ? limitEl.GetInt32() : 10;

        try
        {
            var emails = await _emailService.CheckInboxAsync(folder, limit, ct);

            if (emails.Count == 0)
                return ToolResult.Ok("No emails found.");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {emails.Count} email(s):");
            foreach (var email in emails)
            {
                var read = email.IsRead ? " " : "*";
                sb.AppendLine($"  {read} [{email.MessageId}] {email.Date:yyyy-MM-dd HH:mm} From: {email.From} - {email.Subject}");
            }
            return ToolResult.Ok(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to check email: {ex.Message}");
        }
    }
}

public class EmailReadTool : EmailToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "message_id": { "type": "string", "description": "Email message ID to read" }
            },
            "required": ["message_id"]
        }
        """).RootElement;

    private readonly IEmailService _emailService;

    public EmailReadTool(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public override string Name => "email_read";
    public override string Description => "Read the full content of a specific email";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("message_id", out var idEl))
            return ToolResult.Fail("Missing required parameter: message_id");

        var messageId = idEl.GetString()!;

        try
        {
            var email = await _emailService.ReadEmailAsync(messageId, ct);

            if (email is null)
                return ToolResult.Fail($"Email not found: {messageId}");

            var sb = new StringBuilder();
            sb.AppendLine($"From: {email.From}");
            sb.AppendLine($"To: {email.To}");
            sb.AppendLine($"Subject: {email.Subject}");
            sb.AppendLine($"Date: {email.Date:yyyy-MM-dd HH:mm:ss}");
            if (email.Attachments.Count > 0)
                sb.AppendLine($"Attachments: {string.Join(", ", email.Attachments)}");
            sb.AppendLine();
            sb.Append(email.Body);

            return ToolResult.Ok(sb.ToString());
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read email: {ex.Message}");
        }
    }
}

public class EmailSendAlertTool : EmailToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "to": { "type": "string", "description": "Recipient email address" },
                "subject": { "type": "string", "description": "Email subject" },
                "body": { "type": "string", "description": "Email body (plain text)" }
            },
            "required": ["to", "subject", "body"]
        }
        """).RootElement;

    private readonly IEmailService _emailService;

    public EmailSendAlertTool(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public override string Name => "email_send_alert";
    public override string Description => "Send an alert notification email";
    public override JsonElement ParameterSchema => Schema;

    public override async Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, CancellationToken ct = default)
    {
        if (!arguments.TryGetProperty("to", out var toEl) ||
            !arguments.TryGetProperty("subject", out var subjectEl) ||
            !arguments.TryGetProperty("body", out var bodyEl))
            return ToolResult.Fail("Missing required parameters: to, subject, body");

        var to = toEl.GetString()!;
        var subject = subjectEl.GetString()!;
        var body = bodyEl.GetString()!;

        try
        {
            var sent = await _emailService.SendAlertAsync(to, subject, body, ct);
            return sent
                ? ToolResult.Ok($"Alert sent to {to}")
                : ToolResult.Fail("Failed to send alert. Email service may not be configured.");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to send alert: {ex.Message}");
        }
    }
}
