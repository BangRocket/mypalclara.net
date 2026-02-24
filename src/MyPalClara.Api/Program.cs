using MyPalClara.Api.Middleware;
using MyPalClara.Api.Services;
using MyPalClara.Core;
using MyPalClara.Core.Server;
using MyPalClara.Data;
using MyPalClara.Llm;
using MyPalClara.Memory;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // Register ClaraDbContext
    builder.Services.AddDbContext<ClaraDbContext>(options =>
        ClaraDbContextFactory.Configure(options));

    // Register application services
    builder.Services.AddScoped<UserIdentityService>();

    // Register LLM and Memory systems
    builder.Services.AddClaraLlm();
    builder.Services.AddClaraMemory();

    // Register Gateway (WebSocket server, router, processor)
    builder.Services.AddMyPalClara();

    // CORS
    var corsOrigins = Environment.GetEnvironmentVariable("GATEWAY_API_CORS_ORIGINS")
        ?? "http://localhost:3000,http://localhost:5173";
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // Configure JSON serialization
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
        });

    // Configure Kestrel
    var apiPort = int.TryParse(Environment.GetEnvironmentVariable("CLARA_GATEWAY_API_PORT"), out var p)
        ? p : 18790;
    var wsPort = int.TryParse(Environment.GetEnvironmentVariable("CLARA_GATEWAY_PORT"), out var wp)
        ? wp : 18789;

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(apiPort);
        options.ListenAnyIP(wsPort);
    });

    var app = builder.Build();

    // Wire gateway event handlers (GatewayServer <-> MessageRouter <-> MessageProcessor)
    app.Services.WireGatewayEvents();

    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseWebSockets();

    // WebSocket middleware must come BEFORE auth (WS connections don't use HTTP auth)
    app.UseGatewayWebSocket();

    // Custom auth middleware (skips /api/v1/health)
    app.UseMiddleware<GatewayAuthMiddleware>();

    app.MapControllers();

    Log.Information("Clara Gateway API starting on port {Port}, WebSocket on port {WsPort}", apiPort, wsPort);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Clara Gateway API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
