using Clara.Cli.Voice;
using Clara.Core.Chat;
using Clara.Core.Configuration;
using Clara.Core.Mcp;
using Clara.Core.Memory;
using Spectre.Console;

namespace Clara.Cli.Repl;

/// <summary>Handles ! commands in the REPL.</summary>
public sealed class CommandDispatcher
{
    private readonly McpServerManager _mcp;
    private readonly MemoryService? _memory;
    private readonly ISemanticMemoryStore? _semanticStore;
    private readonly ChatHistoryService? _chatHistory;
    private readonly ClaraConfig _config;
    private readonly IAnsiConsole _console;
    private readonly VoiceManager? _voice;

    /// <summary>Resolved linked user IDs for READ queries. Set by ChatRepl after identity resolution.</summary>
    public IReadOnlyList<string> UserIds { get; set; } = [];

    /// <summary>Resolved user Guids for chat history queries.</summary>
    public IReadOnlyList<Guid> UserGuids { get; set; } = [];

    public CommandDispatcher(
        McpServerManager mcp, ClaraConfig config, IAnsiConsole console,
        MemoryService? memory = null, ISemanticMemoryStore? semanticStore = null,
        ChatHistoryService? chatHistory = null, VoiceManager? voice = null)
    {
        _mcp = mcp;
        _memory = memory;
        _semanticStore = semanticStore;
        _chatHistory = chatHistory;
        _config = config;
        _console = console;
        _voice = voice;
        UserIds = [config.UserId];
    }

    /// <summary>Try to dispatch a command. Returns true if handled.</summary>
    public bool Dispatch(string input)
    {
        if (!input.StartsWith('!')) return false;

        var parts = input[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1] : "";

        // Tier prefixes are NOT commands — they're handled by the REPL
        if (cmd is "high" or "opus" or "mid" or "sonnet" or "low" or "haiku" or "fast")
            return false;

        switch (cmd)
        {
            case "help":
                ShowHelp();
                return true;

            case "mcp":
                HandleMcpCommand(args);
                return true;

            case "memory":
                HandleMemoryCommand(args).GetAwaiter().GetResult();
                return true;

            case "history":
                HandleHistoryCommand().GetAwaiter().GetResult();
                return true;

            case "voice":
                HandleVoiceCommand(args);
                return true;

            case "status":
                ShowStatus();
                return true;

            case "tier":
                _console.MarkupLine("[dim]Tier prefix: use !high, !mid, or !low before your message[/]");
                return true;

            default:
                _console.MarkupLine($"[yellow]Unknown command: !{cmd.EscapeMarkup()}. Type !help for available commands.[/]");
                return true;
        }
    }

    private void ShowHelp()
    {
        var table = new Table().BorderColor(Color.Grey);
        table.AddColumn("Command");
        table.AddColumn("Description");
        table.AddRow("!help", "Show this help");
        table.AddRow("!status", "Show connection status");
        table.AddRow("!mcp list", "List MCP servers");
        table.AddRow("!mcp status", "Show MCP server status");
        table.AddRow("!mcp tools [server]", "List tools (all or for a server)");
        table.AddRow("!history", "Show recent conversations (cross-platform)");
        table.AddRow("!memory search <query>", "Search memories");
        table.AddRow("!memory key", "Show key memories");
        table.AddRow("!memory graph <query>", "Search graph relationships");
        table.AddRow("!voice on|off", "Start/stop voice mode");
        table.AddRow("!tier", "Show tier prefix usage");
        table.AddRow("!high <msg>", "Send with high/opus tier");
        table.AddRow("!mid <msg>", "Send with mid/sonnet tier");
        table.AddRow("!low <msg>", "Send with low/haiku tier");
        table.AddRow("exit/quit/bye", "Exit Clara");
        _console.Write(table);
    }

    private void HandleMcpCommand(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "list";

        switch (subCmd)
        {
            case "list":
            case "status":
                var status = _mcp.GetServerStatus();
                if (status.Count == 0)
                {
                    _console.MarkupLine("[dim]No MCP servers connected.[/]");
                    return;
                }
                var table = new Table().BorderColor(Color.Grey);
                table.AddColumn("Server");
                table.AddColumn("Tools");
                foreach (var (name, toolCount) in status)
                    table.AddRow(name, toolCount.ToString());
                _console.Write(table);
                break;

            case "tools":
                var serverFilter = parts.Length > 1 ? parts[1] : null;
                ShowTools(serverFilter);
                break;

            default:
                _console.MarkupLine($"[yellow]Unknown mcp subcommand: {subCmd.EscapeMarkup()}[/]");
                break;
        }
    }

    private async Task HandleMemoryCommand(string args)
    {
        if (_memory is null)
        {
            _console.MarkupLine("[yellow]Memory system not configured (check database/vector store settings).[/]");
            return;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "help";
        var query = parts.Length > 1 ? parts[1] : "";

        switch (subCmd)
        {
            case "search" when !string.IsNullOrEmpty(query):
                var results = await _memory.SearchAsync(query, UserIds);
                if (results.Count == 0)
                {
                    _console.MarkupLine("[dim]No memories found.[/]");
                    return;
                }
                var table = new Table().BorderColor(Color.Grey).Title("Memory Search Results");
                table.AddColumn("Score");
                table.AddColumn("Memory");
                table.AddColumn("Category");
                foreach (var m in results.Take(10))
                {
                    table.AddRow(
                        $"{m.CompositeScore:F3}",
                        m.Memory.Length > 80 ? m.Memory[..80] + "..." : m.Memory,
                        m.Category ?? "-");
                }
                _console.Write(table);
                break;

            case "key":
                var keyMemories = await _memory.GetKeyMemoriesAsync(UserIds);
                if (keyMemories.Count == 0)
                {
                    _console.MarkupLine("[dim]No key memories found.[/]");
                    return;
                }
                foreach (var m in keyMemories)
                    _console.MarkupLine($"  [bold]KEY[/] {m.Memory.EscapeMarkup()}");
                break;

            case "graph" when !string.IsNullOrEmpty(query):
                await HandleGraphSearchAsync(query);
                break;

            default:
                _console.MarkupLine("[dim]Usage: !memory search <query> | !memory key | !memory graph <query>[/]");
                break;
        }
    }

    private async Task HandleGraphSearchAsync(string query)
    {
        if (_semanticStore is null)
        {
            _console.MarkupLine("[yellow]Semantic store not configured.[/]");
            return;
        }

        var results = await _semanticStore.SearchEntitiesAsync(query, UserIds);
        if (results.Count == 0)
        {
            _console.MarkupLine("[dim]No graph relationships found.[/]");
            return;
        }

        var table = new Table().BorderColor(Color.Grey).Title("Graph Relationships");
        table.AddColumn("Relationship");
        foreach (var r in results.Take(20))
            table.AddRow(r.EscapeMarkup());
        _console.Write(table);
    }

    private async Task HandleHistoryCommand()
    {
        if (_chatHistory is null)
        {
            _console.MarkupLine("[yellow]Chat history not configured (check database settings).[/]");
            return;
        }

        var conversations = await _chatHistory.GetUserConversationsAsync(UserGuids);
        if (conversations.Count == 0)
        {
            _console.MarkupLine("[dim]No conversations found.[/]");
            return;
        }

        var table = new Table().BorderColor(Color.Grey).Title("Recent Conversations");
        table.AddColumn("ID");
        table.AddColumn("Last Activity");
        table.AddColumn("Archived");
        foreach (var c in conversations)
        {
            table.AddRow(
                c.Id.ToString()[..8],
                c.LastActivityAt.ToString("yyyy-MM-dd HH:mm"),
                c.Archived ? "yes" : "no");
        }
        _console.Write(table);
    }

    private void ShowTools(string? serverName)
    {
        if (serverName is not null)
        {
            var tools = _mcp.GetServerTools(serverName);
            if (tools is null)
            {
                _console.MarkupLine($"[yellow]Server '{serverName.EscapeMarkup()}' not found.[/]");
                return;
            }
            var table = new Table().BorderColor(Color.Grey).Title($"Tools: {serverName}");
            table.AddColumn("Name");
            table.AddColumn("Description");
            foreach (var tool in tools)
                table.AddRow(tool.Name, (tool.Description ?? "")[..Math.Min(tool.Description?.Length ?? 0, 60)]);
            _console.Write(table);
        }
        else
        {
            var allSchemas = _mcp.GetAllToolSchemas();
            if (allSchemas.Count == 0)
            {
                _console.MarkupLine("[dim]No tools available.[/]");
                return;
            }
            var table = new Table().BorderColor(Color.Grey).Title("All MCP Tools");
            table.AddColumn("Tool");
            table.AddColumn("Description");
            foreach (var schema in allSchemas)
                table.AddRow(schema.Name, schema.Description[..Math.Min(schema.Description.Length, 60)]);
            _console.Write(table);
        }
    }

    private void HandleVoiceCommand(string args)
    {
        if (_voice is null)
        {
            _console.MarkupLine("[yellow]Voice not configured (check Voice settings in config).[/]");
            return;
        }

        var subCmd = args.Trim().ToLowerInvariant();
        switch (subCmd)
        {
            case "on":
                if (_voice.IsActive)
                {
                    _console.MarkupLine("[dim]Voice mode is already active.[/]");
                    return;
                }
                _voice.Start();
                _console.MarkupLine("[green]Voice mode activated.[/] Speak into your microphone.");
                break;

            case "off":
                if (!_voice.IsActive)
                {
                    _console.MarkupLine("[dim]Voice mode is not active.[/]");
                    return;
                }
                _voice.Stop();
                _console.MarkupLine("[dim]Voice mode deactivated.[/]");
                break;

            default:
                var status = _voice.IsActive ? "[green]active[/]" : "[dim]inactive[/]";
                _console.MarkupLine($"[bold]Voice:[/] {status}");
                _console.MarkupLine("[dim]Usage: !voice on | !voice off[/]");
                break;
        }
    }

    private void ShowStatus()
    {
        var status = _mcp.GetServerStatus();
        _console.MarkupLine($"[bold]MCP Servers:[/] {status.Count} connected, {status.Values.Sum()} tools");
        _console.MarkupLine($"[bold]Memory:[/] {(_memory is not null ? "connected" : "not configured")}");
        _console.MarkupLine($"[bold]Voice:[/] {(_voice?.IsActive == true ? "active" : "inactive")}");
    }
}
