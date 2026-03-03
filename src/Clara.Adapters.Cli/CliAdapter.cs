namespace Clara.Adapters.Cli;

/// <summary>
/// Interactive REPL adapter that connects to the Clara gateway.
/// Reads user input from the terminal, sends to gateway, renders streaming responses.
/// </summary>
public sealed class CliAdapter
{
    private readonly CliGatewayClient _gateway;
    private readonly CliRenderer _renderer;
    private readonly string _sessionKey;
    private readonly string _userId;
    private readonly TaskCompletionSource _responseComplete = new();
    private TaskCompletionSource _currentResponse = new();

    public CliAdapter(CliGatewayClient gateway, CliRenderer renderer, string? userId = null)
    {
        _gateway = gateway;
        _renderer = renderer;
        _userId = userId ?? Environment.UserName;
        _sessionKey = $"clara:main:cli:dm:{_userId}";

        // Wire gateway events
        _gateway.OnTextDelta += OnTextDelta;
        _gateway.OnToolStatus += OnToolStatus;
        _gateway.OnComplete += OnComplete;
        _gateway.OnError += OnError;
    }

    /// <summary>
    /// Run the interactive REPL loop.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _renderer.ShowBanner();

        try
        {
            _renderer.ShowStatus("Connecting to gateway...");
            await _gateway.ConnectAsync(ct);
            _renderer.ShowSuccess("Connected!");
            _renderer.ShowStatus("");

            // Subscribe to our session
            await _gateway.SubscribeAsync(_sessionKey, ct);

            // REPL loop
            while (!ct.IsCancellationRequested)
            {
                var input = _renderer.ReadInput();
                if (input is null)
                    break; // EOF / Ctrl+D

                input = input.Trim();
                if (string.IsNullOrEmpty(input))
                    continue;

                // Handle local commands
                if (input.Equals("/quit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                    _renderer.ShowBanner();
                    continue;
                }

                if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHelp();
                    continue;
                }

                // Send to gateway and wait for response
                _currentResponse = new TaskCompletionSource();
                _renderer.BeginResponse();

                await _gateway.SendMessageAsync(_sessionKey, _userId, "cli", input, ct);

                // Wait for the response to complete (or cancellation)
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(5)); // 5-minute timeout per response

                try
                {
                    await _currentResponse.Task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _renderer.EndResponse();
                    _renderer.ShowError("Response timed out.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
        catch (Exception ex)
        {
            _renderer.ShowConnectionError(ex.Message);
        }
    }

    private void ShowHelp()
    {
        _renderer.ShowStatus("Commands:");
        _renderer.ShowStatus("  /help   - Show this help message");
        _renderer.ShowStatus("  /clear  - Clear the screen");
        _renderer.ShowStatus("  /quit   - Exit the CLI");
        _renderer.ShowStatus("");
    }

    private Task OnTextDelta(string sessionKey, string text)
    {
        if (sessionKey == _sessionKey)
            _renderer.RenderTextDelta(text);
        return Task.CompletedTask;
    }

    private Task OnToolStatus(string sessionKey, string toolName, string status)
    {
        if (sessionKey == _sessionKey)
            _renderer.RenderToolStatus(toolName, status);
        return Task.CompletedTask;
    }

    private Task OnComplete(string sessionKey)
    {
        if (sessionKey == _sessionKey)
        {
            _renderer.EndResponse();
            _currentResponse.TrySetResult();
        }
        return Task.CompletedTask;
    }

    private Task OnError(string sessionKey, string error)
    {
        if (sessionKey == _sessionKey)
        {
            _renderer.EndResponse();
            _renderer.ShowError(error);
            _currentResponse.TrySetResult();
        }
        return Task.CompletedTask;
    }
}
