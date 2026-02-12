using System.Text.Json;
using MyPalClara.Tools.Backfill.Models;

namespace MyPalClara.Tools.Backfill.Parsers;

/// <summary>
/// Parses ChatGPT export JSON files (tree-structured mapping).
/// Walks the tree depth-first, filters to user/assistant messages, pairs into exchanges.
/// </summary>
public static class ChatGptParser
{
    public static List<BackfillConversation> ParseDirectory(string directory)
    {
        var conversations = new List<BackfillConversation>();

        foreach (var file in Directory.GetFiles(directory, "*.json").Order())
        {
            try
            {
                var conversation = ParseFile(file);
                if (conversation is not null && conversation.Exchanges.Count > 0)
                    conversations.Add(conversation);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  WARN: Failed to parse {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return conversations;
    }

    public static BackfillConversation? ParseFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
        var sourceId = Path.GetFileName(filePath);

        if (!root.TryGetProperty("mapping", out var mapping))
            return null;

        // Find root node (parent == null) and walk tree depth-first
        var linearized = LinearizeTree(mapping);

        // Filter to user/assistant messages with content
        var messages = new List<(string Role, string Text, DateTime Timestamp)>();

        foreach (var node in linearized)
        {
            if (!node.TryGetProperty("message", out var message))
                continue;

            if (message.ValueKind == JsonValueKind.Null)
                continue;

            // Skip visually hidden messages
            if (message.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("is_visually_hidden_from_conversation", out var hidden) &&
                hidden.GetBoolean())
                continue;

            if (!message.TryGetProperty("author", out var author) ||
                !author.TryGetProperty("role", out var roleProp))
                continue;

            var role = roleProp.GetString();
            if (role is not ("user" or "assistant"))
                continue;

            var text = ExtractText(message);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var timestamp = DateTime.UnixEpoch;
            if (message.TryGetProperty("create_time", out var createTime) &&
                createTime.ValueKind == JsonValueKind.Number)
            {
                timestamp = DateTime.UnixEpoch.AddSeconds(createTime.GetDouble());
            }

            messages.Add((role, text, timestamp));
        }

        // Pair consecutive user→assistant messages into exchanges
        var exchanges = new List<BackfillExchange>();
        for (var i = 0; i < messages.Count - 1; i++)
        {
            if (messages[i].Role == "user" && messages[i + 1].Role == "assistant")
            {
                exchanges.Add(new BackfillExchange(
                    UserMessage: messages[i].Text,
                    AssistantMessage: messages[i + 1].Text,
                    UserTimestamp: messages[i].Timestamp,
                    AssistantTimestamp: messages[i + 1].Timestamp));
                i++; // skip the assistant message
            }
        }

        return new BackfillConversation(sourceId, "chatgpt", title, exchanges);
    }

    /// <summary>Walk the mapping tree depth-first, returning nodes in conversation order.</summary>
    private static List<JsonElement> LinearizeTree(JsonElement mapping)
    {
        // Build parent→children index and find root
        var childrenMap = new Dictionary<string, List<string>>();
        string? rootId = null;

        foreach (var prop in mapping.EnumerateObject())
        {
            var nodeId = prop.Name;
            var node = prop.Value;

            if (node.TryGetProperty("parent", out var parentProp) &&
                parentProp.ValueKind == JsonValueKind.Null)
            {
                rootId = nodeId;
            }

            if (node.TryGetProperty("children", out var children))
            {
                var childIds = new List<string>();
                foreach (var child in children.EnumerateArray())
                    childIds.Add(child.GetString()!);
                childrenMap[nodeId] = childIds;
            }
        }

        if (rootId is null)
            return [];

        // DFS walk — follow first child at each branch (main conversation path)
        var result = new List<JsonElement>();
        var stack = new Stack<string>();
        stack.Push(rootId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (mapping.TryGetProperty(current, out var node))
            {
                result.Add(node);

                // Push children in reverse order so first child is processed first
                if (childrenMap.TryGetValue(current, out var childIds))
                {
                    for (var i = childIds.Count - 1; i >= 0; i--)
                        stack.Push(childIds[i]);
                }
            }
        }

        return result;
    }

    /// <summary>Extract text content from a ChatGPT message node.</summary>
    private static string ExtractText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return "";

        if (!content.TryGetProperty("parts", out var parts))
            return "";

        var texts = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                texts.Add(part.GetString()!);
            }
            else if (part.ValueKind == JsonValueKind.Object)
            {
                // Audio transcription or other structured content
                if (part.TryGetProperty("text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String)
                {
                    texts.Add(textProp.GetString()!);
                }
            }
        }

        return string.Join("\n", texts).Trim();
    }
}
