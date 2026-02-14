using System.Text.Json;
using MyPalClara.Core.Llm;

namespace MyPalClara.Media;

/// <summary>Provides media-related tools for the agent's tool-calling loop.</summary>
public static class MediaToolProvider
{
    public static IReadOnlyList<ToolSchema> GetTools() =>
    [
        new ToolSchema(
            "media__analyze_image",
            "Analyze an image and describe its contents. Supports URL or file path.",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "source": {
                        "type": "string",
                        "description": "URL or file path of the image to analyze"
                    },
                    "prompt": {
                        "type": "string",
                        "description": "What to look for or describe about the image",
                        "default": "Describe this image in detail."
                    }
                },
                "required": ["source"]
            }
            """).RootElement.Clone()),

        new ToolSchema(
            "media__extract_document",
            "Extract text content from a document file (PDF, TXT, MD, CSV, JSON, XML).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "file_path": {
                        "type": "string",
                        "description": "Path to the document file"
                    }
                },
                "required": ["file_path"]
            }
            """).RootElement.Clone()),

        new ToolSchema(
            "media__download_and_analyze",
            "Download a file from a URL and process it (image analysis or document extraction based on content type).",
            JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": {
                        "type": "string",
                        "description": "URL to download the file from"
                    },
                    "prompt": {
                        "type": "string",
                        "description": "For images, what to look for. Ignored for documents.",
                        "default": "Describe this image in detail."
                    }
                },
                "required": ["url"]
            }
            """).RootElement.Clone()),
    ];
}
