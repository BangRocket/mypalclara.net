using System.Text.Json;
using MyPalClara.Llm;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Memory.FactExtraction;

/// <summary>
/// Extracts facts from user/assistant conversation pairs using a dedicated LLM
/// configured via ROOK_PROVIDER / ROOK_MODEL environment variables.
/// </summary>
public sealed class LlmFactExtractor : IFactExtractor
{
    private const string ExtractionPrompt = """
        You are a Personal Knowledge Management assistant. Your task is to extract important facts, preferences, and information from conversations.

        Given a conversation between a user and an assistant, extract distinct facts that should be remembered for future interactions.

        Rules:
        1. Extract only factual information, preferences, or important context
        2. Each fact should be a single, self-contained statement
        3. Mark facts as "is_key" if they are core identity information (name, location, occupation, relationships, important preferences)
        4. Do NOT extract: pleasantries, small talk, or information the assistant already knows from context
        5. Do NOT extract information about the assistant itself
        6. Deduplicate: don't extract the same fact twice in different words

        Respond with ONLY a JSON object in this exact format:
        {"facts": [{"text": "User prefers dark mode", "is_key": false}, {"text": "User's name is Alice", "is_key": true}]}

        If no facts should be extracted, respond with: {"facts": []}
        """;

    private readonly ILlmProvider _provider;
    private readonly ILogger<LlmFactExtractor> _logger;

    public LlmFactExtractor(ILlmProvider provider, ILogger<LlmFactExtractor> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates an <see cref="ILlmProvider"/> configured for the Rook memory extraction LLM,
    /// using ROOK_PROVIDER / ROOK_MODEL environment variables.
    /// </summary>
    public static ILlmProvider CreateRookProvider(HttpClient httpClient)
    {
        var config = BuildRookConfig();
        return LlmProviderFactory.Create(config, httpClient);
    }

    public async Task<List<ExtractedFact>> ExtractAsync(
        string userMessage,
        string assistantMessage,
        string userId,
        CancellationToken ct = default)
    {
        var messages = new LlmMessage[]
        {
            new SystemMessage(ExtractionPrompt),
            new UserMessage($"User ({userId}): {userMessage}"),
            new AssistantMessage(Content: assistantMessage),
            new UserMessage("Extract facts from the conversation above. Respond with ONLY the JSON object.")
        };

        try
        {
            var response = await _provider.InvokeAsync(messages, ct: ct);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogDebug("LLM returned empty content for fact extraction");
                return [];
            }

            return ParseFacts(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fact extraction LLM call failed for user {UserId}", userId);
            return [];
        }
    }

    private List<ExtractedFact> ParseFacts(string content)
    {
        try
        {
            // Strip markdown code fence if present
            var json = content.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline >= 0)
                    json = json[(firstNewline + 1)..];
                if (json.EndsWith("```"))
                    json = json[..^3];
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("facts", out var factsArray)
                || factsArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogDebug("LLM response missing 'facts' array: {Content}", content);
                return [];
            }

            var facts = new List<ExtractedFact>();

            foreach (var factElement in factsArray.EnumerateArray())
            {
                var text = factElement.TryGetProperty("text", out var textProp)
                    ? textProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var isKey = factElement.TryGetProperty("is_key", out var isKeyProp)
                    && isKeyProp.ValueKind == JsonValueKind.True;

                facts.Add(new ExtractedFact(text, isKey));
            }

            return facts;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse fact extraction response: {Content}", content);
            return [];
        }
    }

    private static LlmConfig BuildRookConfig()
    {
        var provider = GetEnv("ROOK_PROVIDER", "openrouter").ToLowerInvariant();
        var model = GetEnv("ROOK_MODEL", "openai/gpt-4o-mini");

        // Start from the provider's base config, then override with Rook-specific settings
        var config = provider switch
        {
            "anthropic" => new LlmConfig
            {
                Provider = "anthropic",
                ApiKey = GetEnvOrNull("ROOK_API_KEY") ?? GetEnv("ANTHROPIC_API_KEY"),
                BaseUrl = GetEnvOrNull("ROOK_BASE_URL") ?? GetEnvOrNull("ANTHROPIC_BASE_URL"),
                Model = model,
                MaxTokens = 1024,
                Temperature = 0.0f
            },
            "openrouter" => new LlmConfig
            {
                Provider = "openrouter",
                ApiKey = GetEnvOrNull("ROOK_API_KEY") ?? GetEnv("OPENROUTER_API_KEY"),
                BaseUrl = GetEnvOrNull("ROOK_BASE_URL") ?? "https://openrouter.ai/api/v1",
                Model = model,
                MaxTokens = 1024,
                Temperature = 0.0f
            },
            "nanogpt" => new LlmConfig
            {
                Provider = "nanogpt",
                ApiKey = GetEnvOrNull("ROOK_API_KEY") ?? GetEnv("NANOGPT_API_KEY"),
                BaseUrl = GetEnvOrNull("ROOK_BASE_URL") ?? "https://nano-gpt.com/api/v1",
                Model = model,
                MaxTokens = 1024,
                Temperature = 0.0f
            },
            "openai" => new LlmConfig
            {
                Provider = "openai",
                ApiKey = GetEnvOrNull("ROOK_API_KEY") ?? GetEnv("CUSTOM_OPENAI_API_KEY"),
                BaseUrl = GetEnvOrNull("ROOK_BASE_URL") ?? GetEnv("CUSTOM_OPENAI_BASE_URL", "https://api.openai.com/v1"),
                Model = model,
                MaxTokens = 1024,
                Temperature = 0.0f
            },
            _ => throw new InvalidOperationException(
                $"Unknown ROOK_PROVIDER: '{provider}'. Supported: anthropic, openrouter, nanogpt, openai")
        };

        return config;
    }

    private static string GetEnv(string name, string? defaultValue = null)
    {
        return Environment.GetEnvironmentVariable(name)
               ?? defaultValue
               ?? throw new InvalidOperationException(
                   $"Required environment variable '{name}' is not set.");
    }

    private static string? GetEnvOrNull(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
