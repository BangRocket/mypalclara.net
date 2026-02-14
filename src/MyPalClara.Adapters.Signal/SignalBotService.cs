using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MyPalClara.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Adapters.Signal;

/// <summary>
/// Manages the signal-cli child process using JSON-RPC over stdin/stdout.
/// </summary>
public sealed class SignalBotService : IHostedService, IDisposable
{
    private readonly ClaraConfig _config;
    private readonly SignalMessageHandler _handler;
    private readonly ILogger<SignalBotService> _logger;
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public SignalBotService(ClaraConfig config, SignalMessageHandler handler, ILogger<SignalBotService> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var psi = new ProcessStartInfo
        {
            FileName = _config.Signal.SignalCliPath,
            Arguments = $"-a {_config.Signal.AccountPhone} --output=json jsonRpc",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = Process.Start(psi);
        if (_process is null)
        {
            _logger.LogError("Failed to start signal-cli process");
            return Task.CompletedTask;
        }

        _logger.LogInformation("signal-cli started (PID {Pid})", _process.Id);
        _readTask = ReadLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Signal bot stopping...");
        _cts?.Cancel();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error killing signal-cli process");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Send a JSON-RPC request to signal-cli via stdin.</summary>
    public async Task SendJsonRpcAsync(string method, object @params, CancellationToken ct)
    {
        if (_process?.StandardInput is null) return;

        var rpc = new { jsonrpc = "2.0", method, @params, id = Guid.NewGuid().ToString("N") };
        var json = JsonSerializer.Serialize(rpc);

        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_process?.StandardOutput is null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line is null) break; // Process exited

                try
                {
                    await _handler.HandleJsonRpcMessageAsync(line, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing signal-cli message");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "signal-cli read loop failed");
        }

        _logger.LogInformation("signal-cli read loop ended");
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _process?.Dispose();
    }
}
