using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Relay.Shared;

namespace LocalWebSocketBroker;

/// <summary>
/// Stand-in for the Azure Relay gateway. It holds the single OUTBOUND WebSocket
/// opened by machine M and multiplexes many Service S requests over it,
/// correlating replies by <c>correlationId</c>.
/// </summary>
public sealed class TunnelRegistry(ILogger<TunnelRegistry> logger)
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentResponse>> _pending = new();

    private WebSocket? _tunnel;

    public bool IsOnline => _tunnel?.State == WebSocketState.Open;

    public async Task HoldTunnelAsync(WebSocket socket, CancellationToken ct)
    {
        _tunnel = socket;
        logger.LogInformation("[handshake] Listener tunnel registered. Broker is now a reachable front door for machine M.");

        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var response = JsonSerializer.Deserialize<AgentResponse>(json);
                if (response is not null && _pending.TryRemove(response.CorrelationId, out var waiter))
                    waiter.TrySetResult(response);
                else
                    logger.LogWarning("Received a reply with no pending request: {Json}", json);
            }
        }
        finally
        {
            _tunnel = null;
            logger.LogWarning("[handshake] Listener tunnel removed; machine M is unreachable until it dials back out.");
            foreach (var kvp in _pending)
                if (_pending.TryRemove(kvp.Key, out var waiter))
                    waiter.TrySetException(new IOException("Tunnel closed before a reply arrived."));
        }
    }

    public async Task<AgentResponse> DispatchAsync(AgentRequest request, TimeSpan timeout, CancellationToken ct)
    {
        var socket = _tunnel;
        if (socket is null || socket.State != WebSocketState.Open)
            throw new InvalidOperationException("No listener tunnel is online: machine M has not dialled out yet.");

        var waiter = new TaskCompletionSource<AgentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.CorrelationId] = waiter;

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

        // One writer at a time: WebSocket frames from concurrent senders must not interleave.
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await waiter.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(request.CorrelationId, out _);
            throw new TimeoutException($"No reply for correlationId {request.CorrelationId} within {timeout}.");
        }
    }
}
