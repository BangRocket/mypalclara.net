using System.Text.Json;
using MyPalClara.Core.Llm;

namespace MyPalClara.Browser.Automation;

/// <summary>Exposes browser automation actions as LLM-callable tools.</summary>
public static class BrowserToolProvider
{
    public static List<ToolSchema> GetToolSchemas()
    {
        return
        [
            new ToolSchema(
                "browser__navigate",
                "Navigate the browser to a URL",
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "url": { "type": "string", "description": "The URL to navigate to" }
                    },
                    "required": ["url"]
                }
                """).RootElement),

            new ToolSchema(
                "browser__snapshot",
                "Get a text snapshot of the current page content",
                JsonDocument.Parse("""{ "type": "object", "properties": {} }""").RootElement),

            new ToolSchema(
                "browser__click",
                "Click an element on the page by CSS selector",
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "selector": { "type": "string", "description": "CSS selector of the element to click" }
                    },
                    "required": ["selector"]
                }
                """).RootElement),

            new ToolSchema(
                "browser__type",
                "Type text into an input field by CSS selector",
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "selector": { "type": "string", "description": "CSS selector of the input field" },
                        "text": { "type": "string", "description": "Text to type into the field" }
                    },
                    "required": ["selector", "text"]
                }
                """).RootElement),

            new ToolSchema(
                "browser__evaluate",
                "Execute JavaScript in the browser and return the result",
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "expression": { "type": "string", "description": "JavaScript expression to evaluate" }
                    },
                    "required": ["expression"]
                }
                """).RootElement),
        ];
    }

    public static async Task<string> ExecuteToolAsync(
        BrowserAutomation browser, string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        return toolName switch
        {
            "browser__navigate" => await browser.NavigateAsync(
                arguments.GetProperty("url").GetString()!, ct),

            "browser__snapshot" => await browser.GetSnapshotAsync(ct),

            "browser__click" => await browser.ClickAsync(
                arguments.GetProperty("selector").GetString()!, ct),

            "browser__type" => await browser.TypeAsync(
                arguments.GetProperty("selector").GetString()!,
                arguments.GetProperty("text").GetString()!, ct),

            "browser__evaluate" => await browser.EvaluateAsync(
                arguments.GetProperty("expression").GetString()!, ct),

            _ => $"Error: Unknown browser tool '{toolName}'",
        };
    }
}
