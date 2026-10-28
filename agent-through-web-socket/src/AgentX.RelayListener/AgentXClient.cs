using System.Diagnostics;
using System.Text.Json;
using Relay.Shared;

namespace AgentX.RelayListener;

/// <summary>
/// Loopback HTTP client. This is the ONLY component that can reach Agent X,
/// because Agent X binds 127.0.0.1:5001 and machine M has no inbound route.
/// </summary>
public sealed class AgentXClient(HttpClient http, ILogger<AgentXClient> logger)
{
    public async Task<AgentResponse> InvokeAsync(AgentRequest request, CancellationToken ct)
    {
        var path = request.Op switch
        {
            RelayOps.GetAiTools => "/get_ai_tools",
            RelayOps.Health => "/health",
            _ => $"/ask?q={Uri.EscapeDataString(request.Query ?? string.Empty)}"
        };

        var sw = Stopwatch.StartNew();
        try
        {
            using var res = await http.GetAsync(path, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();

            logger.LogInformation("Loopback call {Path} -> {Status} in {Ms} ms", path, (int)res.StatusCode, sw.ElapsedMilliseconds);

            return new AgentResponse
            {
                CorrelationId = request.CorrelationId,
                StatusCode = (int)res.StatusCode,
                Payload = body,
                UpstreamLatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Loopback call {Path} failed", path);
            return new AgentResponse
            {
                CorrelationId = request.CorrelationId,
                StatusCode = 502,
                Error = $"Agent X unreachable on loopback: {ex.Message}",
                UpstreamLatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    public static AgentRequest Parse(string json) =>
        JsonSerializer.Deserialize<AgentRequest>(json) ?? new AgentRequest { Op = RelayOps.Health };

    public static string Serialize(AgentResponse response) => JsonSerializer.Serialize(response);
}
