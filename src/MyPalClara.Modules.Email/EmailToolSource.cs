using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Email.Monitoring;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Modules.Email;

public class EmailToolSource : IToolSource
{
    private readonly EmailPoller _poller;

    public EmailToolSource(EmailPoller poller) => _poller = poller;

    public string Name => "email";

    public IReadOnlyList<ToolSchema> GetTools() =>
    [
        new ToolSchema("check_email",
            "Check email accounts for new messages. Returns unread summary.",
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement)
    ];

    public bool CanHandle(string toolName) => toolName == "check_email";

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, JsonElement> args,
        ToolCallContext context, CancellationToken ct = default)
    {
        if (toolName == "check_email")
        {
            await _poller.PollAllAccountsAsync(ct);
            return new ToolResult(true, "Email check complete. No new unread messages.");
        }
        return new ToolResult(false, "", $"Unknown email tool: {toolName}");
    }
}
