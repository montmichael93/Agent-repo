using System.Net.WebSockets;
using LocalWebSocketBroker;
using Relay.Shared;

// Offline stand-in for the Azure Relay gateway, so the two flows can be demonstrated
// end-to-end without an Azure subscription. Port 7000 is the only publicly reachable
// socket in the whole topology.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TunnelRegistry>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

var listenerSecret = app.Configuration["ListenerSecret"] ?? "dev-listener-secret";
var senderSecret = app.Configuration["SenderSecret"] ?? "dev-sender-secret";

app.MapGet("/", (TunnelRegistry registry) => Results.Ok(new
{
    role = "relay-gateway-stand-in",
    tunnelOnline = registry.IsOnline
}));

// FLOW 1: machine M dials out here and parks the tunnel (HTTP Upgrade -> WebSocket).
app.MapGet("/listen", async (HttpContext ctx, TunnelRegistry registry, ILogger<Program> logger) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("Expected a WebSocket upgrade request.");

    if (ctx.Request.Headers["X-Relay-Secret"] != listenerSecret)
    {
        logger.LogWarning("Rejected listener handshake from {Ip}: bad secret.", ctx.Connection.RemoteIpAddress);
        return Results.Unauthorized();
    }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await registry.HoldTunnelAsync(socket, ctx.RequestAborted);
    return Results.Empty;
});

// FLOW 2: Service S posts a request; the broker pushes it through the parked tunnel.
app.MapPost("/dispatch", async (HttpContext ctx, AgentRequest request, TunnelRegistry registry) =>
{
    if (ctx.Request.Headers["X-Relay-Send-Key"] != senderSecret)
        return Results.Unauthorized();

    try
    {
        var response = await registry.DispatchAsync(request, TimeSpan.FromSeconds(30), ctx.RequestAborted);
        return Results.Json(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new AgentResponse
        {
            CorrelationId = request.CorrelationId,
            StatusCode = 503,
            Error = ex.Message
        }, statusCode: 503);
    }
    catch (TimeoutException ex)
    {
        return Results.Json(new AgentResponse
        {
            CorrelationId = request.CorrelationId,
            StatusCode = 504,
            Error = ex.Message
        }, statusCode: 504);
    }
});

app.Run();
