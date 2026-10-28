using Relay.Shared;

namespace ServiceS.Api;

/// <summary>
/// Service S never knows machine M's IP. It only knows a rendezvous endpoint
/// (the relay namespace) and an identity that is authorized to send to it.
/// </summary>
public interface IAgentXGateway
{
    Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken ct);
}
