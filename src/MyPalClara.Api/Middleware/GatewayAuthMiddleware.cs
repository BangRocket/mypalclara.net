using MyPalClara.Data;
using Microsoft.EntityFrameworkCore;

namespace MyPalClara.Api.Middleware;

public class GatewayAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _gatewaySecret;

    public GatewayAuthMiddleware(RequestDelegate next)
    {
        _next = next;
        _gatewaySecret = Environment.GetEnvironmentVariable("CLARA_GATEWAY_SECRET");
    }

    public async Task InvokeAsync(HttpContext context, ClaraDbContext db)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip auth for health endpoints
        if (path.StartsWith("/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Check gateway secret if configured
        if (!string.IsNullOrEmpty(_gatewaySecret))
        {
            var secretHeader = context.Request.Headers["X-Gateway-Secret"].FirstOrDefault();
            if (string.IsNullOrEmpty(secretHeader) || secretHeader != _gatewaySecret)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing gateway secret" });
                return;
            }
        }

        // Require canonical user ID header
        var canonicalUserId = context.Request.Headers["X-Canonical-User-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(canonicalUserId))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing X-Canonical-User-Id header" });
            return;
        }

        // Look up user in DB
        var user = await db.CanonicalUsers.FirstOrDefaultAsync(u => u.Id == canonicalUserId);
        if (user == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "User not found" });
            return;
        }

        // Store user in HttpContext for downstream use
        context.Items["CurrentUser"] = user;

        await _next(context);
    }
}
