using Spectre.Console;

namespace Clara.Adapters.Cli;

/// <summary>
/// Renders Clara responses to the terminal using Spectre.Console.
/// </summary>
public sealed class CliRenderer
{
    private bool _isFirstDelta = true;

    /// <summary>
    /// Display the welcome banner.
    /// </summary>
    public void ShowBanner()
    {
        AnsiConsole.Write(new FigletText("Clara").Color(Color.Blue));
        AnsiConsole.MarkupLine("[dim]Type a message to chat. Ctrl+C to exit.[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Show a status message (connecting, etc.).
    /// </summary>
    public void ShowStatus(string message)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Show a success message.
    /// </summary>
    public void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Show an error message.
    /// </summary>
    public void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Prompt the user for input.
    /// </summary>
    public string? ReadInput()
    {
        AnsiConsole.Markup("[bold blue]You:[/] ");
        return Console.ReadLine();
    }

    /// <summary>
    /// Begin rendering a response (print the "Clara:" prefix).
    /// </summary>
    public void BeginResponse()
    {
        _isFirstDelta = true;
        AnsiConsole.Markup("[bold green]Clara:[/] ");
    }

    /// <summary>
    /// Render a streaming text delta.
    /// </summary>
    public void RenderTextDelta(string text)
    {
        if (_isFirstDelta)
            _isFirstDelta = false;

        // Write raw text to console (streaming, no markup parsing)
        Console.Write(text);
    }

    /// <summary>
    /// Show a tool execution status inline.
    /// </summary>
    public void RenderToolStatus(string toolName, string status)
    {
        var color = status switch
        {
            "start" or "started" => "yellow",
            "end" or "completed" => "green",
            "error" or "failed" => "red",
            _ => "dim",
        };
        AnsiConsole.MarkupLine($"\n  [{color}]>> {Markup.Escape(toolName)}: {Markup.Escape(status)}[/]");
    }

    /// <summary>
    /// End the current response (newlines after the response).
    /// </summary>
    public void EndResponse()
    {
        Console.WriteLine();
        Console.WriteLine();
    }

    /// <summary>
    /// Show a connection error.
    /// </summary>
    public void ShowConnectionError(string error)
    {
        AnsiConsole.MarkupLine($"[red bold]Connection error:[/] [red]{Markup.Escape(error)}[/]");
    }
}
