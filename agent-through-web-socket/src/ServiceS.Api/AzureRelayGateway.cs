using System.Text;
using Microsoft.Azure.Relay;
using Microsoft.Extensions.Options;
using Relay.Shared;

namespace ServiceS.Api;

/// <summary>
/// Sender side of the hybrid connection.
///
/// Service S opens an OUTBOUND TLS connection to the relay namespace and asks for a
/// rendezvous with the hybrid connection "agent-x". The relay gateway:
///   1. validates the Send token (SAS rule or Entra ID role assignment),
///   2. checks that a listener is currently online for that hybrid connection,
///   3. tells the listener to dial a rendezvous socket,
///   4. splices the two sockets into one bidirectional byte tunnel.
/// Neither side ever accepts an inbound connection.
/// </summary>
public sealed class AzureRelayGateway(
    IOptions<RelayOptions> options,
    ILogger<AzureRelayGateway> logger) : IAgentXGateway
{
    private readonly RelayOptions _options = options.Value;

    public async Task<AgentResponse> SendAsync(AgentRequest request, CancellationToken ct)
    {
        TokenProvider tokenProvider = _options.UseEntraId
            ? TokenProvider.CreateManagedIdentityTokenProvider()
            : TokenProvider.CreateSharedAccessSignatureTokenProvider(_options.KeyName, _options.Key);

        var client = new HybridConnectionClient(_options.ListenerWebSocketUri, tokenProvider);

        logger.LogInformation("[send] Requesting rendezvous on {Uri} for corr={Corr}", _options.HybridConnectionUri, request.CorrelationId);

        await using var stream = await client.CreateConnectionAsync();

        await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(request));

        var replyLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(replyLine))
        {
            return new AgentResponse
            {
                CorrelationId = request.CorrelationId,
                StatusCode = 502,
                Error = "Empty reply from the relay tunnel."
            };
        }

        return System.Text.Json.JsonSerializer.Deserialize<AgentResponse>(replyLine)
               ?? new AgentResponse { CorrelationId = request.CorrelationId, StatusCode = 502, Error = "Unparsable reply." };
    }
}
