using Clara.Cli.Voice;
using Clara.Core.Chat;
using Clara.Core.Configuration;
using Clara.Core.Identity;
using Clara.Core.Llm;
using Clara.Core.Mcp;
using Clara.Core.Memory;
using Clara.Core.Orchestration;
using Clara.Core.Personality;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Clara.Cli.Repl;

/// <summary>Main REPL loop for the Clara CLI.</summary>
public sealed class ChatRepl
{
    private readonly ClaraConfig _config;
    private readonly LlmOrchestrator _orchestrator;
    private readonly McpServerManager _mcpManager;
    private readonly PersonalityLoader _personality;
    private readonly MemoryService? _memory;
    private readonly UserIdentityService? _identity;
    private readonly ChatHistoryService? _chatHistory;
    private readonly VoiceManager? _voice;
    private readonly CommandDispatcher _commands;
    private readonly StreamingRenderer _renderer;
    private readonly IAnsiConsole _console;
    private readonly ILogger<ChatRepl> _logger;

    // Resolved linked user IDs (set once in RunAsync)
    private IReadOnlyList<string> _allUserIds = [];
    private Guid? _userGuid;

    // Conversation history — load up to 50 messages, trim by token budget
    private const int MaxHistoryMessages = 50;
    // ~25% of 200K context for history, ~4 chars per token
    private const int HistoryCharBudget = 200_000;
    private readonly List<ChatMessage> _history = [];

    // Tier prefix detection
    private static readonly Dictionary<string, string> TierPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["!high"] = "high", ["!opus"] = "high",
        ["!mid"] = "mid", ["!sonnet"] = "mid",
        ["!low"] = "low", ["!haiku"] = "low", ["!fast"] = "low",
    };

    public ChatRepl(
        ClaraConfig config,
        LlmOrchestrator orchestrator,
        McpServerManager mcpManager,
        PersonalityLoader personality,
        CommandDispatcher commands,
        StreamingRenderer renderer,
        IAnsiConsole console,
        ILogger<ChatRepl> logger,
        MemoryService? memory = null,
        UserIdentityService? identity = null,
        ChatHistoryService? chatHistory = null,
        VoiceManager? voice = null)
    {
        _config = config;
        _orchestrator = orchestrator;
        _mcpManager = mcpManager;
        _personality = personality;
        _commands = commands;
        _renderer = renderer;
        _console = console;
        _logger = logger;
        _memory = memory;
        _identity = identity;
        _chatHistory = chatHistory;
        _voice = voice;

        // Wire voice transcription callback
        if (_voice is not null)
            _voice.OnTranscription = OnVoiceTranscriptionAsync;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Resolve cross-platform identity (once at startup)
        if (_identity is not null)
        {
            await _identity.EnsurePlatformLinkAsync(_config.UserId, linkTo: _config.LinkTo);
            _allUserIds = await _identity.ResolveAllUserIdsAsync(_config.UserId);
            _userGuid = await _identity.ResolveUserGuidAsync(_config.UserId);
            _logger.LogDebug("Resolved {Count} linked user IDs for {UserId}", _allUserIds.Count, _config.UserId);
        }
        else
        {
            _allUserIds = [_config.UserId];
        }

        // Share resolved IDs with command dispatcher
        _commands.UserIds = _allUserIds;
        if (_userGuid.HasValue)
            _commands.UserGuids = [_userGuid.Value];

        // Restore conversation and chat history from DB
        if (_chatHistory is not null && _userGuid.HasValue)
        {
            try
            {
                var channelResult = await _chatHistory.EnsureCliChannelAsync(_userGuid.Value, ct);
                if (channelResult.HasValue)
                {
                    var (_, channelId) = channelResult.Value;
                    var conversationId = await _chatHistory.GetOrCreateConversationAsync(channelId, _userGuid.Value, ct);
                    if (conversationId.HasValue)
                    {
                        var dbMessages = await _chatHistory.LoadRecentMessagesAsync(conversationId.Value, MaxHistoryMessages, ct);
                        if (dbMessages.Count > 0)
                        {
                            _history.AddRange(dbMessages);
                            _logger.LogDebug("Loaded {Count} messages from conversation {ConversationId}", dbMessages.Count, conversationId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat history restore failed, continuing without history");
            }
        }

        ShowWelcome();

        while (!ct.IsCancellationRequested)
        {
            _console.Write(new Markup("[bold green]You:[/] "));
            var input = Console.ReadLine();

            if (input is null || input.Trim().ToLowerInvariant() is "exit" or "quit" or "bye")
            {
                // Finalize emotional context before exiting
                if (_memory is not null)
                {
                    try
                    {
                        await _memory.FinalizeSessionAsync(_config.UserId, "cli", ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Session finalization failed");
                    }
                }

                _console.MarkupLine("[dim]Goodbye![/]");
                break;
            }

            input = input.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            // Command dispatch
            if (_commands.Dispatch(input)) continue;

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
        // Fetch memory context (if available)
        MemoryContext? memoryCtx = null;
        if (_memory is not null)
        {
            try
            {
                memoryCtx = await _memory.FetchContextAsync(input, _allUserIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Memory context fetch failed, continuing without memory");
            }
        }

        // Build messages with memory context
        var messages = BuildMessages(input, memoryCtx);

        // Get tools
        var tools = _mcpManager.GetAllToolSchemas();

        // Add user message to history
        _history.Add(new UserMessage(input));

        // Track sentiment
        _memory?.TrackSentiment(_config.UserId, "cli", input);

        _renderer.ShowThinking();

        var fullText = "";
        var firstChunk = true;

        await foreach (var evt in _orchestrator.GenerateWithToolsAsync(messages, tools, tier, ct: ct))
        {
            switch (evt)
            {
                case TextChunkEvent chunk:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        _console.Write(new Markup("[bold cyan]" + _config.Bot.Name + ":[/] "));
                        firstChunk = false;
                    }
                    _renderer.WriteChunk(chunk.Text);
                    fullText += chunk.Text;
                    break;

                case ToolStartEvent toolStart:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        firstChunk = false;
                    }
                    _renderer.ShowToolStart(toolStart.ToolName, toolStart.Step);
                    break;

                case ToolResultEvent toolResult:
                    _renderer.ShowToolResult(toolResult.ToolName, toolResult.Success, toolResult.OutputPreview);
                    break;

                case CompleteEvent complete:
                    if (firstChunk)
                    {
                        _renderer.ClearLine();
                        _console.Write(new Markup("[bold cyan]" + _config.Bot.Name + ":[/] "));
                    }
                    fullText = complete.FullText;

                    // TTS: synthesize and play if voice is active
                    if (_voice?.IsActive == true && !string.IsNullOrEmpty(fullText))
                    {
                        _ = _voice.SpeakAsync(fullText, ct);
                    }
                    break;
            }
        }

        _renderer.FinishStream();

        // Add to history
        _history.Add(new AssistantMessage(fullText));

        // Trim history: drop oldest messages until within budget
        TrimHistory();

        // Background: persist exchange to DB
        if (_chatHistory?.CurrentConversationId is not null && !string.IsNullOrEmpty(fullText))
        {
            var conversationId = _chatHistory.CurrentConversationId.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _chatHistory.StoreExchangeAsync(conversationId, _userGuid, input, fullText, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background chat history persist failed");
                }
            }, ct);
        }

        // Background: memory add + promote used memories
        if (_memory is not null && !string.IsNullOrEmpty(fullText))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _memory.AddAsync(input, fullText, _config.UserId, ct);

                    // Promote memories that were part of the context
                    if (memoryCtx is not null)
                    {
                        var usedIds = memoryCtx.RelevantMemories
                            .Concat(memoryCtx.KeyMemories)
                            .Select(m => m.Id);
                        await _memory.PromoteUsedMemoriesAsync(usedIds, _allUserIds, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background memory processing failed");
                }
            }, ct);
        }
    }

    private List<ChatMessage> BuildMessages(string currentInput, MemoryContext? memoryCtx)
    {
        var messages = new List<ChatMessage>();

        // System: personality
        messages.Add(new SystemMessage(_personality.GetPersonality()));

        // System: memory context sections
        if (memoryCtx is not null)
        {
            var sections = MemoryService.BuildPromptSections(memoryCtx);
            foreach (var section in sections)
                messages.Add(new SystemMessage(section));
        }

        // History
        messages.AddRange(_history);

        // Current user message
        messages.Add(new UserMessage(currentInput));

        return messages;
    }

    private void TrimHistory()
    {
        // Hard cap on message count
        while (_history.Count > MaxHistoryMessages * 2)
            _history.RemoveAt(0);

        // Trim oldest messages until within char budget
        while (_history.Count > 2)
        {
            var totalChars = 0;
            foreach (var msg in _history)
                totalChars += msg.Content?.Length ?? 0;

            if (totalChars <= HistoryCharBudget)
                break;

            _history.RemoveAt(0);
        }
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
