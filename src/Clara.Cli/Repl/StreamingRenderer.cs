using Spectre.Console;

namespace Clara.Cli.Repl;

/// <summary>Renders streaming LLM output using Spectre.Console Live display.</summary>
public sealed class StreamingRenderer
{
    private readonly IAnsiConsole _console;

    public StreamingRenderer(IAnsiConsole console)
    {
        _console = console;
    }

    /// <summary>Show a thinking spinner with optional status text.</summary>
    public void ShowThinking(string status = "Thinking...")
    {
        _console.Markup($"[dim]{status.EscapeMarkup()}[/]");
    }

    /// <summary>Clear the current line (e.g. to remove the thinking indicator).</summary>
    public void ClearLine()
    {
        _console.Write("\r");
        _console.Write(new string(' ', _console.Profile.Width));
        _console.Write("\r");
    }

    /// <summary>Write a text chunk inline (streaming output).</summary>
    public void WriteChunk(string text)
    {
        _console.Write(text);
    }

    /// <summary>Finish the current streaming block with a newline.</summary>
    public void FinishStream()
    {
        _console.WriteLine();
    }

    /// <summary>Show a tool start notification.</summary>
    public void ShowToolStart(string toolName, int step)
    {
        _console.MarkupLine($"\n  [yellow]>> {toolName.EscapeMarkup()} (step {step})...[/]");
    }

    /// <summary>Show a tool result notification.</summary>
    public void ShowToolResult(string toolName, bool success, string preview)
    {
        var icon = success ? "[green]OK[/]" : "[red]ERR[/]";
        _console.MarkupLine($"  [dim]{icon} {toolName.EscapeMarkup()}[/]");
    }

    /// <summary>Show an error message.</summary>
    public void ShowError(string message)
    {
        _console.MarkupLine($"[red]{message.EscapeMarkup()}[/]");
    }

    /// <summary>Show an info message.</summary>
    public void ShowInfo(string message)
    {
        _console.MarkupLine($"[dim]{message.EscapeMarkup()}[/]");
    }
}
