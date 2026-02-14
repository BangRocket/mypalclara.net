using Spectre.Console;
using MyPalClara.Core.Configuration;
using MyPalClara.Core.Protocol;

namespace MyPalClara.App.Terminal;

public sealed class TerminalChatApp
{
    private readonly GatewayClient _gateway;
    private readonly ClaraConfig _config;
    private readonly List<(string Role, string Text)> _history = [];

    public TerminalChatApp(GatewayClient gateway, ClaraConfig config)
    {
        _gateway = gateway;
        _config = config;
    }

    public async Task RunAsync()
    {
        AnsiConsole.Write(new FigletText("Clara").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[dim]Terminal Chat Client[/]");
        AnsiConsole.MarkupLine("[dim]Type /quit to exit, /clear to clear history, /status for connection info[/]\n");

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Connecting to Gateway...", async ctx =>
                {
                    await _gateway.ConnectAsync();
                });
            AnsiConsole.MarkupLine("[green]Connected to Gateway.[/]\n");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to connect: {Markup.Escape(ex.Message)}[/]");
            return;
        }

        while (true)
        {
            AnsiConsole.Markup("[bold aqua]You:[/] ");
            var input = Console.ReadLine();
            if (input is null) break;

            input = input.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            switch (input.ToLowerInvariant())
            {
                case "/quit":
                case "/exit":
                    AnsiConsole.MarkupLine("[dim]Goodbye![/]");
                    await _gateway.DisposeAsync();
                    return;

                case "/clear":
                    _history.Clear();
                    AnsiConsole.Clear();
                    AnsiConsole.MarkupLine("[dim]Chat cleared.[/]\n");
                    continue;

                case "/status":
                    ShowStatus();
                    continue;
            }

            _history.Add(("user", input));

            var responseText = "";
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("aqua"))
                .StartAsync("Clara is thinking...", async ctx =>
                {
                    var request = new ChatRequest(
                        ChannelId: "terminal",
                        ChannelName: "Terminal",
                        ChannelType: "dm",
                        UserId: _config.UserId,
                        DisplayName: "User",
                        Content: input);

                    await foreach (var response in _gateway.ChatAsync(request))
                    {
                        switch (response)
                        {
                            case TextChunk chunk:
                                responseText += chunk.Text;
                                ctx.Status($"Clara is thinking... ({responseText.Length} chars)");
                                break;
                            case Complete complete:
                                responseText = complete.FullText;
                                break;
                            case ErrorMessage error:
                                responseText = $"Error: {error.Message}";
                                break;
                        }
                    }
                });

            if (!string.IsNullOrEmpty(responseText))
            {
                _history.Add(("assistant", responseText));
                RenderResponse(responseText);
            }

            AnsiConsole.WriteLine();
        }

        await _gateway.DisposeAsync();
    }

    private void RenderResponse(string text)
    {
        var panel = new Panel(Markup.Escape(text))
            .Header("[bold mediumpurple3]Clara[/]")
            .BorderColor(Color.MediumPurple3)
            .Padding(1, 0);
        AnsiConsole.Write(panel);
    }

    private void ShowStatus()
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Property");
        table.AddColumn("Value");
        table.AddRow("Connected", _gateway.IsConnected ? "[green]Yes[/]" : "[red]No[/]");
        table.AddRow("Gateway", $"{_config.Gateway.Host}:{_config.Gateway.Port}");
        table.AddRow("User", _config.UserId);
        table.AddRow("Messages", _history.Count.ToString());
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
