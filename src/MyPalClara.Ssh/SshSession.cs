using System.Text;
using FxSsh.Services;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Ssh;

/// <summary>
/// Per-connection REPL session over SSH.
/// Text I/O only (no Spectre.Console — raw terminal).
/// Reads lines from the SSH channel, sends ChatRequest to Gateway, renders responses.
/// </summary>
public sealed class SshSession
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly SessionChannel _channel;
    private readonly string _username;
    private readonly ILogger<SshSession> _logger;

    // Line buffer for reading input character by character
    private readonly StringBuilder _lineBuffer = new();

    // Tier prefix detection
    private static readonly Dictionary<string, string> TierPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["!high"] = "high", ["!opus"] = "high",
        ["!mid"] = "mid", ["!sonnet"] = "mid",
        ["!low"] = "low", ["!haiku"] = "low", ["!fast"] = "low",
    };

    public SshSession(
        GatewayClient gateway,
        ClaraConfig config,
        SessionChannel channel,
        string username,
        ILogger<SshSession> logger)
    {
        _gateway = gateway;
        _config = config;
        _channel = channel;
        _username = username;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        // Welcome banner
        SendLine($"\r\nMyPalClara SSH Session — Welcome, {_username}!");
        SendLine("Type 'exit' to disconnect, '!help' for commands.\r\n");
        SendPrompt();

        // Set up line-oriented input from the SSH channel
        var inputLines = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var closed = new TaskCompletionSource();

        _channel.DataReceived += (_, data) =>
        {
            foreach (var b in data)
            {
                switch (b)
                {
                    case (byte)'\r' or (byte)'\n':
                        // Echo newline
                        _channel.SendData("\r\n"u8.ToArray());
                        var line = _lineBuffer.ToString();
                        _lineBuffer.Clear();
                        inputLines.Writer.TryWrite(line);
                        break;

                    case 0x7F or (byte)'\b': // Backspace/Delete
                        if (_lineBuffer.Length > 0)
                        {
                            _lineBuffer.Remove(_lineBuffer.Length - 1, 1);
                            _channel.SendData("\b \b"u8.ToArray()); // Erase character
                        }
                        break;

                    case 0x03: // Ctrl+C
                        inputLines.Writer.TryWrite("");
                        break;

                    case 0x04: // Ctrl+D
                        inputLines.Writer.TryComplete();
                        break;

                    default:
                        if (b >= 0x20) // Printable characters
                        {
                            _lineBuffer.Append((char)b);
                            _channel.SendData([(byte)b]); // Echo
                        }
                        break;
                }
            }
        };

        _channel.CloseReceived += (_, _) =>
        {
            inputLines.Writer.TryComplete();
            closed.TrySetResult();
        };

        _channel.EofReceived += (_, _) =>
        {
            inputLines.Writer.TryComplete();
        };

        // Main REPL loop
        try
        {
            await foreach (var input in inputLines.Reader.ReadAllAsync())
            {
                var trimmed = input.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    SendPrompt();
                    continue;
                }

                if (trimmed.ToLowerInvariant() is "exit" or "quit" or "bye")
                {
                    SendLine("Goodbye!");
                    break;
                }

                if (trimmed == "!help")
                {
                    ShowHelp();
                    SendPrompt();
                    continue;
                }

                // Tier prefix detection
                string? tier = null;
                var firstSpace = trimmed.IndexOf(' ');
                if (firstSpace > 0)
                {
                    var prefix = trimmed[..firstSpace];
                    if (TierPrefixes.TryGetValue(prefix, out var detectedTier))
                    {
                        tier = detectedTier;
                        trimmed = trimmed[(firstSpace + 1)..].Trim();
                    }
                }

                try
                {
                    await ProcessMessageAsync(trimmed, tier);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing SSH message");
                    SendLine($"Error: {ex.Message}");
                }

                SendPrompt();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal
        }
    }

    private async Task ProcessMessageAsync(string input, string? tier)
    {
        var request = new ChatRequest(
            ChannelId: $"ssh-{_username}",
            ChannelName: "SSH",
            ChannelType: "text",
            UserId: $"ssh-{_username}",
            DisplayName: _username,
            Content: input,
            Tier: tier);

        var firstChunk = true;

        await foreach (var response in _gateway.ChatAsync(request))
        {
            switch (response)
            {
                case TextChunk textChunk:
                    if (firstChunk)
                    {
                        Send($"{_config.Bot.Name}: ");
                        firstChunk = false;
                    }
                    Send(textChunk.Text);
                    break;

                case ToolStart toolStart:
                    SendLine($"  >> {toolStart.ToolName} (step {toolStart.Step})...");
                    break;

                case ToolResult toolResult:
                    var icon = toolResult.Success ? "OK" : "ERR";
                    SendLine($"  {icon} {toolResult.ToolName}");
                    break;

                case Complete complete:
                    if (firstChunk)
                        Send($"{_config.Bot.Name}: {complete.FullText}");
                    SendLine("");
                    break;

                case ErrorMessage error:
                    SendLine($"Error: {error.Message}");
                    break;
            }
        }

        if (firstChunk)
            SendLine(""); // Ensure newline if no output
    }

    private void ShowHelp()
    {
        SendLine("Commands:");
        SendLine("  !help         — Show this help");
        SendLine("  !high <msg>   — Send with high/opus tier");
        SendLine("  !mid <msg>    — Send with mid/sonnet tier");
        SendLine("  !low <msg>    — Send with low/haiku tier");
        SendLine("  exit          — Disconnect");
    }

    private void SendPrompt()
    {
        Send("You: ");
    }

    private void Send(string text)
    {
        // Replace \n with \r\n for proper terminal rendering
        var normalized = text.Replace("\n", "\r\n");
        _channel.SendData(Encoding.UTF8.GetBytes(normalized));
    }

    private void SendLine(string text)
    {
        Send(text + "\r\n");
    }
}
