using System.Text.Json.Serialization;

namespace Relay.Shared;

/// <summary>
/// Application-level envelope that travels as a single WebSocket text frame
/// over the Azure Relay hybrid connection tunnel.
/// </summary>
public sealed record AgentRequest
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>Logical operation, e.g. "get_ai_tools" or "ask".</summary>
    [JsonPropertyName("op")]
    public string Op { get; init; } = "ask";

    /// <summary>Caller query (the "q" of /check_with_x?q=...).</summary>
    [JsonPropertyName("q")]
    public string? Query { get; init; }

    [JsonPropertyName("sentAtUtc")]
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentResponse
{
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; } = 200;

    /// <summary>Raw payload returned by Agent X (JSON as string).</summary>
    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("servedByMachine")]
    public string ServedByMachine { get; init; } = Environment.MachineName;

    [JsonPropertyName("upstreamLatencyMs")]
    public long UpstreamLatencyMs { get; init; }
}

public static class RelayOps
{
    public const string GetAiTools = "get_ai_tools";
    public const string Ask = "ask";
    public const string Health = "health";
}
