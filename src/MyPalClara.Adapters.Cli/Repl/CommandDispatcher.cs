using System.Text.Json;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Voice;
using Spectre.Console;

namespace MyPalClara.Adapters.Cli.Repl;

/// <summary>
/// Handles ! commands in the REPL.
/// Local commands (help, voice, quit) execute locally.
/// Remote commands (memory, mcp, status, history) send CommandRequest to Gateway.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly IAnsiConsole _console;
    private readonly VoiceManager? _voice;

    public CommandDispatcher(
        GatewayClient gateway, ClaraConfig config, IAnsiConsole console,
        VoiceManager? voice = null)
    {
        _gateway = gateway;
        _config = config;
        _console = console;
        _voice = voice;
    }

    /// <summary>Try to dispatch a command. Returns true if handled.</summary>
    public async Task<bool> DispatchAsync(string input, CancellationToken ct = default)
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
            // Local commands
            case "help":
                ShowHelp();
                return true;

            case "voice":
                HandleVoiceCommand(args);
                return true;

            case "tier":
                _console.MarkupLine("[dim]Tier prefix: use !high, !mid, or !low before your message[/]");
                return true;

            // Remote commands — forwarded to Gateway
            case "status":
                await HandleRemoteCommandAsync("status", null, ct);
                return true;

            case "mcp":
                await HandleRemoteCommandAsync("mcp-status", null, ct);
                return true;

            case "memory":
                await HandleMemoryCommandAsync(args, ct);
                return true;

            case "history":
                await HandleRemoteCommandAsync("history", null, ct);
                return true;

            default:
                _console.MarkupLine($"[yellow]Unknown command: !{cmd.EscapeMarkup()}. Type !help for available commands.[/]");
                return true;
        }
    }

    private async Task HandleMemoryCommandAsync(string args, CancellationToken ct)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "help";
        var query = parts.Length > 1 ? parts[1] : "";

        switch (subCmd)
        {
            case "search" when !string.IsNullOrEmpty(query):
                await HandleRemoteCommandAsync("memory-search",
                    new Dictionary<string, JsonElement>
                    {
                        ["query"] = JsonSerializer.SerializeToElement(query),
                        ["userId"] = JsonSerializer.SerializeToElement(_config.UserId),
                    }, ct);
                break;

            case "key":
                await HandleRemoteCommandAsync("memory-key",
                    new Dictionary<string, JsonElement>
                    {
                        ["userId"] = JsonSerializer.SerializeToElement(_config.UserId),
                    }, ct);
                break;

            case "graph" when !string.IsNullOrEmpty(query):
                await HandleRemoteCommandAsync("memory-graph",
                    new Dictionary<string, JsonElement>
                    {
                        ["query"] = JsonSerializer.SerializeToElement(query),
                        ["userId"] = JsonSerializer.SerializeToElement(_config.UserId),
                    }, ct);
                break;

            default:
                _console.MarkupLine("[dim]Usage: !memory search <query> | !memory key | !memory graph <query>[/]");
                break;
        }
    }

    private async Task HandleRemoteCommandAsync(string command, Dictionary<string, JsonElement>? args, CancellationToken ct)
    {
        try
        {
            var request = new CommandRequest(command, args, _config.UserId);
            var result = await _gateway.CommandAsync(request, ct);

            if (result.Success)
            {
                if (result.Data.HasValue)
                    _console.MarkupLine($"[dim]{result.Data.Value.ToString().EscapeMarkup()}[/]");
                else
                    _console.MarkupLine($"[green]{command}: OK[/]");
            }
            else
            {
                _console.MarkupLine($"[red]{command}: {(result.Error ?? "unknown error").EscapeMarkup()}[/]");
            }
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Command failed: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private void ShowHelp()
    {
        var table = new Table().BorderColor(Color.Grey);
        table.AddColumn("Command");
        table.AddColumn("Description");
        table.AddRow("!help", "Show this help");
        table.AddRow("!status", "Show connection status");
        table.AddRow("!mcp", "Show MCP server status");
        table.AddRow("!history", "Show recent conversations");
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
}
