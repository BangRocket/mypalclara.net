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
    private readonly CommandDispatcher _commands;
    private readonly StreamingRenderer _renderer;
    private readonly IAnsiConsole _console;
    private readonly ILogger<ChatRepl> _logger;

    // Resolved linked user IDs (set once in RunAsync)
    private IReadOnlyList<string> _allUserIds = [];

    // Conversation history (last N messages)
    private const int ContextMessageCount = 15;
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
        ChatHistoryService? chatHistory = null)
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
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Resolve cross-platform identity (once at startup)
        if (_identity is not null)
        {
            await _identity.EnsurePlatformLinkAsync(_config.UserId, linkTo: _config.LinkTo);
            _allUserIds = await _identity.ResolveAllUserIdsAsync(_config.UserId);
            _logger.LogDebug("Resolved {Count} linked user IDs for {UserId}", _allUserIds.Count, _config.UserId);
        }
        else
        {
            _allUserIds = [_config.UserId];
        }

        // Share resolved IDs with command dispatcher
        _commands.UserIds = _allUserIds;

        // Restore session and chat history from DB
        if (_chatHistory is not null)
        {
            try
            {
                var contextId = ChatHistoryService.BuildCliContextId(_config.UserId);
                var sessionId = await _chatHistory.GetOrCreateSessionAsync(_config.UserId, contextId, ct);
                if (sessionId is not null)
                {
                    var dbMessages = await _chatHistory.LoadRecentMessagesAsync(sessionId, ContextMessageCount, ct);
                    if (dbMessages.Count > 0)
                    {
                        _history.AddRange(dbMessages);
                        _logger.LogDebug("Loaded {Count} messages from session {SessionId}", dbMessages.Count, sessionId);
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
                // Touch session activity on exit
                if (_chatHistory?.CurrentSessionId is not null)
                {
                    try
                    {
                        await _chatHistory.UpdateSessionActivityAsync(_chatHistory.CurrentSessionId, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Session activity update failed");
                    }
                }

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
                    break;
            }
        }

        _renderer.FinishStream();

        // Add to history
        _history.Add(new AssistantMessage(fullText));

        // Trim history
        while (_history.Count > ContextMessageCount * 2)
            _history.RemoveAt(0);

        // Background: persist exchange to DB
        if (_chatHistory?.CurrentSessionId is not null && !string.IsNullOrEmpty(fullText))
        {
            var sessionId = _chatHistory.CurrentSessionId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _chatHistory.StoreExchangeAsync(sessionId, _config.UserId, input, fullText, ct);
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

    private void ShowWelcome()
    {
        _console.MarkupLine("[dim]Type !help for commands, exit to quit[/]");
        _console.WriteLine();
    }
}
