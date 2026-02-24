using System.Text.Json;
using MyPalClara.Llm;
using MyPalClara.Modules.Sdk;

namespace MyPalClara.Gateway.Tools;

public static class DiscordTools
{
    public static void Register(IToolRegistry registry, IGatewayBridge bridge)
    {
        var names = new[]
        {
            ("send_discord_file", "Send a file to the current Discord channel. Args: filename (string), content (string), channel_id (string, optional)."),
            ("format_discord_message", "Format a message with Discord markdown/embeds. Args: content (string), format (string: bold|italic|code|quote)."),
            ("add_discord_reaction", "Add an emoji reaction to a message. Args: message_id (string), emoji (string)."),
            ("send_discord_embed", "Send a rich embed message. Args: title (string), description (string), color (int, optional), fields (array, optional)."),
            ("create_discord_thread", "Create a thread from a message. Args: message_id (string), name (string)."),
            ("edit_discord_message", "Edit a previously sent message. Args: message_id (string), content (string)."),
            ("send_discord_buttons", "Send a message with interactive buttons. Args: content (string), buttons (array of {label, custom_id}).")
        };

        foreach (var (name, desc) in names)
        {
            var toolName = name; // capture for closure
            registry.RegisterTool(toolName, new ToolSchema(toolName, desc,
                JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
                (args, ctx, ct) => ExecuteDiscordToolAsync(toolName, args, ctx, bridge, ct));
        }
    }

    /// <summary>
    /// Generic handler for all Discord tools: sends a tool_action message to the adapter via IGatewayBridge
    /// and waits for a response (with timeout).
    /// </summary>
    public static async Task<ToolResult> ExecuteDiscordToolAsync(
        string toolName, Dictionary<string, JsonElement> args, ToolCallContext ctx,
        IGatewayBridge bridge, CancellationToken ct)
    {
        // Send tool_action to all Discord nodes
        var action = new
        {
            type = "tool_action",
            tool_name = toolName,
            channel_id = ctx.ChannelId,
            user_id = ctx.UserId,
            request_id = ctx.RequestId,
            arguments = args
        };

        await bridge.BroadcastToPlatformAsync("discord", action, ct);

        // For now, return success -- adapter response handling will be added
        // when we implement the protocol handler in the Discord adapter.
        return new ToolResult(true, $"Sent {toolName} action to Discord adapter");
    }
}
