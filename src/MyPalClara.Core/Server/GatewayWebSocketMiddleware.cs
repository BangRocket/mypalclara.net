using MyPalClara.Core.Processing;
using MyPalClara.Core.Router;
using MyPalClara.Llm;
using MyPalClara.Memory;
using MyPalClara.Memory.FactExtraction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPalClara.Core.Server;

/// <summary>
/// ASP.NET Core middleware that accepts WebSocket connections at "/" or "/ws"
/// and hands them to the GatewayServer.
/// </summary>
public class GatewayWebSocketMiddleware
{
    private readonly RequestDelegate _next;

    public GatewayWebSocketMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (context.WebSockets.IsWebSocketRequest && (path is "/" or "/ws"))
        {
            var gatewayServer = context.RequestServices.GetRequiredService<GatewayServer>();
            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            await gatewayServer.HandleConnectionAsync(ws, context.RequestAborted);
        }
        else
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Extension methods for wiring up the gateway WebSocket middleware.
/// </summary>
public static class GatewayWebSocketExtensions
{
    /// <summary>
    /// Adds the gateway WebSocket middleware to the pipeline.
    /// Requires <see cref="GatewayServer"/> to be registered as a singleton service.
    /// </summary>
    public static IApplicationBuilder UseGatewayWebSocket(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GatewayWebSocketMiddleware>();
    }

    /// <summary>
    /// Registers the gateway server and node registry as singleton services.
    /// </summary>
    public static IServiceCollection AddMyPalClara(this IServiceCollection services)
    {
        services.AddSingleton<NodeRegistry>();
        services.AddSingleton<GatewayServer>();
        services.AddSingleton<MessageRouter>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<MessageProcessor>(sp =>
        {
            var server = sp.GetRequiredService<GatewayServer>();
            return new MessageProcessor(
                sp.GetRequiredService<ILlmProvider>(),
                sp.GetRequiredService<IRookMemory>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                server.SendAsync,  // matches SendMessageDelegate signature
                sp.GetRequiredService<SessionManager>(),
                sp.GetRequiredService<IFactExtractor>(),
                sp.GetRequiredService<SmartIngestion>(),
                sp.GetRequiredService<ILogger<MessageProcessor>>());
        });
        return services;
    }
}
