using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Options;
using Relay.Shared;

namespace AgentX.RelayListener;

/// <summary>
/// Offline / no-Azure-subscription mode. Instead of the Azure Relay gateway we dial
/// <see cref="LocalWebSocketBrokerOptions.Uri"/> (the LocalWebSocketBroker project),
/// which plays exactly the same role: a publicly reachable rendezvous point that both
/// sides reach with OUTBOUND connections.
///
/// Networking concepts exercised here:
///  * HTTP/1.1 "Upgrade: websocket" handshake (RFC 6455) over TLS.
///  * Bearer secret passed in a header during the upgrade, never in a frame.
///  * One logical message per WebSocket text frame, length-delimited by the frame
///    header (no manual framing needed) + EndOfMessage flag for fragmented frames.
///  * Ping/pong keepalive so NAT mappings on machine M's edge router stay alive.
///  * Reconnect with exponential backoff + jitter.
/// </summary>
public sealed class LocalSocketListenerWorker(
    IOptions<LocalWebSocketBrokerOptions> brokerOptions,
    AgentXClient agentClient,
    ILogger<LocalSocketListenerWorker> logger) : BackgroundService
{
    private readonly LocalWebSocketBrokerOptions _broker = brokerOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PumpAsync(stoppingToken);
                attempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))))
                            + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                logger.LogWarning(ex, "Tunnel dropped (attempt {Attempt}); reconnecting in {Delay}.", attempt, delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = _broker.KeepAliveInterval;
        socket.Options.SetRequestHeader("X-Relay-Secret", _broker.ListenerSecret);

        var uri = new Uri($"{_broker.Uri}/listen");
        logger.LogInformation("[handshake] Outbound WebSocket upgrade -> {Uri}", uri);
        await socket.ConnectAsync(uri, ct);
        logger.LogInformation("[handshake] Tunnel ONLINE (state={State}). Machine M is now reachable through the broker.", socket.State);

        var buffer = new byte[16 * 1024];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var json = await ReceiveTextAsync(socket, buffer, ct);
            if (json is null)
            {
                logger.LogInformation("Broker closed the tunnel.");
                return;
            }

            var request = AgentXClient.Parse(json);
            logger.LogInformation("[request] corr={Corr} op={Op} q={Q}", request.CorrelationId, request.Op, request.Query);

            var response = await agentClient.InvokeAsync(request, ct);
            var payload = Encoding.UTF8.GetBytes(AgentXClient.Serialize(response));
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

public sealed class LocalWebSocketBrokerOptions
{
    public const string SectionName = "LocalBroker";

    public string Uri { get; set; } = "ws://localhost:7000";
    public string ListenerSecret { get; set; } = "dev-listener-secret";
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}
