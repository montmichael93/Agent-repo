namespace Relay.Shared;

/// <summary>
/// Shared connection settings for both sides of the hybrid connection.
/// </summary>
public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    /// <summary>e.g. "my-agent-relay.servicebus.windows.net"</summary>
    public string Namespace { get; set; } = "my-agent-relay.servicebus.windows.net";

    /// <summary>Hybrid connection (entity) name, e.g. "agent-x".</summary>
    public string ConnectionName { get; set; } = "agent-x";

    /// <summary>SAS rule name: listener side uses a Listen-only rule, sender side a Send-only rule.</summary>
    public string KeyName { get; set; } = "RootManageSharedAccessKey";

    public string Key { get; set; } = "replace-me";

    /// <summary>
    /// When true, use Microsoft Entra ID (managed identity / workload identity)
    /// instead of SAS keys. Preferred for production.
    /// </summary>
    public bool UseEntraId { get; set; }

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Local address where the dummy AI Agent X actually listens.</summary>
    public string AgentBaseAddress { get; set; } = "https://localhost:5001";

    /// <summary>Application-level keepalive interval for the tunnel (NAT/idle-timeout defence).</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    public Uri HybridConnectionUri =>
        new($"sb://{Namespace}/{ConnectionName}");

    /// <summary>wss:// endpoint used for the outbound WebSocket upgrade handshake.</summary>
    public Uri ListenerWebSocketUri =>
        new($"wss://{Namespace}/$hc/{ConnectionName}");

    public Uri SenderHttpUri =>
        new($"https://{Namespace}/{ConnectionName}");
}
