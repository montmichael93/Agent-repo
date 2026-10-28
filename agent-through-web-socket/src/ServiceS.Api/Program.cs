using Relay.Shared;
using ServiceS.Api;

// Service S: the consumer. It exposes /check_with_x?q=... to its own callers and
// fulfils them by talking to Agent X through the relay tunnel.

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));

var mode = builder.Configuration["Mode"] ?? "LocalBroker";

if (string.Equals(mode, "AzureRelay", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAgentXGateway, AzureRelayGateway>();
}
else
{
    var brokerUri = builder.Configuration["LocalBroker:HttpUri"] ?? "http://localhost:7000";
    var senderKey = builder.Configuration["LocalBroker:SenderSecret"] ?? "dev-sender-secret";

    builder.Services.AddHttpClient<IAgentXGateway, LocalBrokerGateway>(client =>
    {
        client.BaseAddress = new Uri(brokerUri);
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Add("X-Relay-Send-Key", senderKey);
    });
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "service-s",
    mode,
    endpoints = new[] { "/check_with_x?q=...", "/check_with_x/tools", "/check_with_x/health" }
}));

// Primary endpoint requested in the problem statement.
app.MapGet("/check_with_x", async (string? q, IAgentXGateway gateway, CancellationToken ct) =>
{
    var request = new AgentRequest { Op = RelayOps.Ask, Query = q };
    return await RespondAsync(gateway, request, ct);
});

app.MapGet("/check_with_x/tools", async (IAgentXGateway gateway, CancellationToken ct) =>
{
    var request = new AgentRequest { Op = RelayOps.GetAiTools };
    return await RespondAsync(gateway, request, ct);
});

app.MapGet("/check_with_x/health", async (IAgentXGateway gateway, CancellationToken ct) =>
{
    var request = new AgentRequest { Op = RelayOps.Health };
    return await RespondAsync(gateway, request, ct);
});

app.Run();

static async Task<IResult> RespondAsync(IAgentXGateway gateway, AgentRequest request, CancellationToken ct)
{
    try
    {
        var response = await gateway.SendAsync(request, ct);
        return Results.Json(new
        {
            correlationId = response.CorrelationId,
            viaTunnel = true,
            servedByMachine = response.ServedByMachine,
            upstreamLatencyMs = response.UpstreamLatencyMs,
            agentStatusCode = response.StatusCode,
            agentPayload = response.Payload,
            error = response.Error
        }, statusCode: response.StatusCode is >= 200 and < 600 ? response.StatusCode : 502);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            correlationId = request.CorrelationId,
            viaTunnel = false,
            error = ex.Message
        }, statusCode: 502);
    }
}
