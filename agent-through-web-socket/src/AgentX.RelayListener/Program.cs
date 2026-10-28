using AgentX.RelayListener;
using Relay.Shared;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));
builder.Services.Configure<LocalWebSocketBrokerOptions>(builder.Configuration.GetSection(LocalWebSocketBrokerOptions.SectionName));

var relayOptions = new RelayOptions();
builder.Configuration.GetSection(RelayOptions.SectionName).Bind(relayOptions);

builder.Services.AddHttpClient<AgentXClient>(client =>
    {
        client.BaseAddress = new Uri(relayOptions.AgentBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Dev only: Agent X uses the ASP.NET Core self-signed dev certificate on loopback.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

// "Mode": AzureRelay (real hybrid connection) or LocalBroker (offline demo).
var mode = builder.Configuration["Mode"] ?? "LocalBroker";

if (string.Equals(mode, "AzureRelay", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHostedService<AzureRelayListenerWorker>();
else
    builder.Services.AddHostedService<LocalSocketListenerWorker>();

var host = builder.Build();
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup")
    .LogInformation("AgentX.RelayListener starting in {Mode} mode. Agent X base address: {Base}", mode, relayOptions.AgentBaseAddress);

host.Run();
