using System.Net.Http.Json;
using Relay.Shared;

namespace ServiceS.Api;

/// <summary>
/// Offline sender: POSTs the envelope to the LocalWebSocketBroker, which pushes it
/// through the WebSocket that machine M parked earlier. Same topology as Azure Relay,
/// no subscription required.
/// </summary>
public sealed class LocalBrokerGateway(HttpClient http, ILogger<LocalBrokerGateway> logger) : IAgentXGateway
{
    public async Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken ct)
    {
        logger.LogInformation("[send] Dispatching corr={Corr} op={Op} via broker {Base}", request.CorrelationId, request.Op, http.BaseAddress);

        using var res = await http.PostAsJsonAsync("/dispatch", request, ct);
        var response = await res.Content.ReadFromJsonAsync<AgentResponse>(cancellationToken: ct);

        return response ?? new AgentResponse
        {
            CorrelationId = request.CorrelationId,
            StatusCode = (int)res.StatusCode,
            Error = "Broker returned an unparsable response."
        };
    }
}
