using System.Text;
using Microsoft.Azure.Relay;
using Microsoft.Extensions.Options;
using Relay.Shared;

namespace AgentX.RelayListener;

/// <summary>
/// FLOW 1 (handshake): machine M dials OUT to the Azure Relay gateway, authenticates
/// with a Listen-only SAS token, and parks a long-lived control WebSocket.
/// FLOW 2 (request):   the gateway pushes a rendezvous request down that control channel;
///                     we accept it, read the frame, call loopback, write the reply frame.
/// </summary>
public sealed class AzureRelayListenerWorker(
    IOptions<RelayOptions> options,
    AgentXClient agentClient,
    ILogger<AzureRelayListenerWorker> logger) : BackgroundService
{
    private readonly RelayOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunListenerAsync(stoppingToken);
                attempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Networking concept: the tunnel is a TCP connection and will die
                // (NAT idle timeout, proxy recycle, gateway node drain). Reconnect with
                // exponential backoff + jitter so a fleet of machines does not stampede.
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 6))))
                            + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
                logger.LogWarning(ex, "Relay listener dropped (attempt {Attempt}). Reconnecting in {Delay}.", attempt, delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task RunListenerAsync(CancellationToken ct)
    {
        TokenProvider tokenProvider = _options.UseEntraId
            // In production prefer Entra ID: no shared secret ever touches the machine.
            // TokenProvider.CreateManagedIdentityTokenProvider() requires Microsoft.Azure.Relay
            // + a managed identity with the "Azure Relay Listener" role.
            ? TokenProvider.CreateManagedIdentityTokenProvider()
            : TokenProvider.CreateSharedAccessSignatureTokenProvider(_options.KeyName, _options.Key);

        var listener = new HybridConnectionListener(_options.ListenerWebSocketUri, tokenProvider)
        {
            // Application-level keepalive: small control frames keep NATs and the
            // relay gateway from reaping an idle tunnel.
            KeepAliveInterval = _options.KeepAliveInterval
        };

        listener.Connecting += (_, _) => logger.LogInformation("[handshake] TLS + WebSocket upgrade to {Uri} ...", _options.ListenerWebSocketUri);
        listener.Online += (_, _) => logger.LogInformation("[handshake] Control channel ONLINE. Machine M is now addressable via the relay.");
        listener.Offline += (_, _) => logger.LogWarning("[handshake] Control channel OFFLINE.");

        await listener.OpenAsync(ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Each accepted connection is a *rendezvous* WebSocket: the gateway
                // asked us to dial a second outbound socket dedicated to one request.
                var stream = await listener.AcceptConnectionAsync();
                if (stream is null)
                {
                    logger.LogInformation("Listener closed by relay.");
                    return;
                }

                _ = HandleRendezvousAsync(stream, ct);
            }
        }
        finally
        {
            await listener.CloseAsync(CancellationToken.None);
        }
    }

    private async Task HandleRendezvousAsync(HybridConnectionStream stream, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) return;

            var request = AgentXClient.Parse(line);
            logger.LogInformation("[request] corr={Corr} op={Op} q={Q}", request.CorrelationId, request.Op, request.Query);

            var response = await agentClient.InvokeAsync(request, ct);
            await writer.WriteLineAsync(AgentXClient.Serialize(response));

            await stream.ShutdownAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rendezvous connection failed.");
        }
        finally
        {
            await stream.CloseAsync(CancellationToken.None);
        }
    }
}
