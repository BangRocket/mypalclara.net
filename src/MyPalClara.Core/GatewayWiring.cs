namespace MyPalClara.Core;

using MyPalClara.Core.Protocol;
using MyPalClara.Core.Router;
using MyPalClara.Core.Server;
using MyPalClara.Core.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class GatewayWiring
{
    /// <summary>
    /// Connect the GatewayServer, MessageRouter, and MessageProcessor event handlers.
    /// Call after app.Build() but before app.Run().
    /// </summary>
    public static void WireGatewayEvents(this IServiceProvider services)
    {
        var server = services.GetRequiredService<GatewayServer>();
        var router = services.GetRequiredService<MessageRouter>();
        var processor = services.GetRequiredService<MessageProcessor>();
        var logger = services.GetRequiredService<ILogger<GatewayServer>>();

        // GatewayServer.OnMessageReceived -> Router.SubmitAsync
        server.OnMessageReceived += async (msg, ws, platform) =>
        {
            var request = new QueuedRequest
            {
                RequestId = msg.Id,
                ChannelId = msg.Channel.Id,
                UserId = msg.User.Id,
                Content = msg.Content,
                WebSocket = ws,
                NodeId = server.NodeRegistry.GetByWebSocket(ws)?.NodeId ?? "unknown",
                RawRequest = default // Could serialize if needed
            };

            var isMention = msg.Channel.Type == "dm" ||
                            msg.Metadata?.ContainsKey("is_mention") == true;

            var (acquired, position) = await router.SubmitAsync(request, isMention: isMention);

            if (acquired)
            {
                _ = ProcessRequestAsync(processor, request, msg, platform, logger);
            }
            else if (position == -1)
            {
                await server.SendErrorAsync(ws, msg.Id, "duplicate", "Duplicate message rejected");
            }
            else
            {
                await server.SendAsync(ws, new { type = "queued", request_id = msg.Id, position });
            }
        };

        // GatewayServer.OnCancelReceived -> Router.CancelAsync
        server.OnCancelReceived += async (cancelMsg, ws) =>
        {
            var cancelled = await router.CancelAsync(cancelMsg.RequestId);
            if (cancelled)
            {
                await server.SendAsync(ws, new CancelledMessage(cancelMsg.RequestId));
            }
        };

        // MessageRouter.OnRequestReady -> Process dequeued request
        router.OnRequestReady += async queuedRequest =>
        {
            var node = server.NodeRegistry.GetByWebSocket(queuedRequest.WebSocket);
            var platform = node?.Platform ?? "unknown";

            // We don't have the original MessageRequest here, so build a minimal context
            await ProcessQueuedRequestAsync(processor, queuedRequest, platform, logger);
        };
    }

    private static async Task ProcessRequestAsync(
        MessageProcessor processor,
        QueuedRequest request,
        MessageRequest msg,
        string platform,
        ILogger logger)
    {
        try
        {
            var context = new ProcessingContext
            {
                RequestId = msg.Id,
                ResponseId = Guid.NewGuid().ToString(),
                UserId = msg.User.Id,
                ChannelId = msg.Channel.Id,
                ChannelType = msg.Channel.Type,
                Content = msg.Content,
                Platform = platform,
                WebSocket = request.WebSocket,
                DisplayName = msg.User.DisplayName ?? msg.User.Name,
                GuildId = msg.Channel.GuildId,
                TierOverride = msg.TierOverride
            };

            await processor.ProcessAsync(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process request {RequestId}", request.RequestId);
        }
    }

    private static async Task ProcessQueuedRequestAsync(
        MessageProcessor processor,
        QueuedRequest request,
        string platform,
        ILogger logger)
    {
        try
        {
            var context = new ProcessingContext
            {
                RequestId = request.RequestId,
                ResponseId = Guid.NewGuid().ToString(),
                UserId = request.UserId,
                ChannelId = request.ChannelId,
                ChannelType = request.ChannelId.StartsWith("dm-") ? "dm" : "server",
                Content = request.Content,
                Platform = platform,
                WebSocket = request.WebSocket
            };

            await processor.ProcessAsync(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process queued request {RequestId}", request.RequestId);
        }
    }
}
