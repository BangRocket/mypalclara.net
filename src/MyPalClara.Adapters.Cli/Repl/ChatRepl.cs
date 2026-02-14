using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;
using MyPalClara.Voice;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace MyPalClara.Adapters.Cli.Repl;

/// <summary>
/// Main REPL loop for the Clara CLI.
/// Thin client: sends ChatRequest to Gateway via GatewayClient,
/// renders streamed GatewayResponse events.
/// </summary>
public sealed class ChatRepl
{
    private readonly ClaraConfig _config;
    private readonly GatewayClient _gateway;
    private readonly CommandDispatcher _commands;
    private readonly StreamingRenderer _renderer;
    private readonly VoiceManager? _voice;
    private readonly IAnsiConsole _console;
    private readonly ILogger<ChatRepl> _logger;

    // Tier prefix detection
    private static readonly Dictionary<string, string> TierPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["!high"] = "high", ["!opus"] = "high",
        ["!mid"] = "mid", ["!sonnet"] = "mid",
        ["!low"] = "low", ["!haiku"] = "low", ["!fast"] = "low",
    };

    public ChatRepl(
        ClaraConfig config,
        GatewayClient gateway,
        CommandDispatcher commands,
        StreamingRenderer renderer,
        IAnsiConsole console,
        ILogger<ChatRepl> logger,
        VoiceManager? voice = null)
    {
        _config = config;
        _gateway = gateway;
        _commands = commands;
        _renderer = renderer;
        _console = console;
        _logger = logger;
        _voice = voice;

        // Wire voice transcription callback
        if (_voice is not null)
            _voice.OnTranscription = OnVoiceTranscriptionAsync;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        ShowWelcome();

        while (!ct.IsCancellationRequested)
        {
            _console.Write(new Markup("[bold green]You:[/] "));
            var input = Console.ReadLine();

            if (input is null || input.Trim().ToLowerInvariant() is "exit" or "quit" or "bye")
            {
                _console.MarkupLine("[dim]Goodbye![/]");
                break;
            }

            input = input.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            // Command dispatch (handles both local and remote commands)
            if (await _commands.DispatchAsync(input, ct)) continue;

            // Tier prefix detection
            string? tier = null;
            var firstSpace = input.IndexOf(' ');
            if (firstSpace > 0)
            {
                var prefix = input[..firstSpace];
                if (TierPrefixes.TryGetValue(prefix, out var detectedTier))
                {
                    tier = detectedTier;
                    input = input[(firstSpace + 1)..].Trim();
                    _renderer.ShowInfo($"Using tier: {tier}");
                }
            }

            try
            {
                await ProcessMessageAsync(input, tier, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                _renderer.ShowError($"Error: {ex.Message}");
            }
        }
    }

    private async Task ProcessMessageAsync(string input, string? tier, CancellationToken ct)
    {
        var request = new ChatRequest(
            ChannelId: "cli-main",
            ChannelName: "CLI",
            ChannelType: "text",
            UserId: _config.UserId,
            DisplayName: _config.Bot.Name,
            Content: input,
            Tier: tier);

        _renderer.ShowThinking();
        var firstChunk = true;

        await foreach (var response in _gateway.ChatAsync(request, ct))
        {
            switch (response)
            {
                case TextChunk textChunk:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        _console.Write(new Markup("[bold cyan]" + _config.Bot.Name + ":[/] "));
                        firstChunk = false;
                    }
                    _renderer.WriteChunk(textChunk.Text);
                    break;

                case ToolStart toolStart:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        firstChunk = false;
                    }
                    _renderer.ShowToolStart(toolStart.ToolName, toolStart.Step);
                    break;

                case ToolResult toolResult:
                    _renderer.ShowToolResult(toolResult.ToolName, toolResult.Success, toolResult.Preview);
                    break;

                case Complete complete:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        _console.Write(new Markup("[bold cyan]" + _config.Bot.Name + ":[/] "));
                    }

                    // TTS: synthesize and play if voice is active
                    if (_voice?.IsActive == true && !string.IsNullOrEmpty(complete.FullText))
                    {
                        _ = _voice.SpeakAsync(complete.FullText, ct);
                    }
                    break;

                case ErrorMessage error:
                    _renderer.ShowError($"Gateway error: {error.Message}");
                    break;
            }
        }

        _renderer.FinishStream();
    }

    private async Task OnVoiceTranscriptionAsync(string text)
    {
        _console.MarkupLine($"[dim]You (voice):[/] {text.EscapeMarkup()}");
        try
        {
            await ProcessMessageAsync(text, tier: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice input");
            _renderer.ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowWelcome()
    {
        _console.MarkupLine("[dim]Type !help for commands, exit to quit[/]");
        _console.WriteLine();
    }
}
