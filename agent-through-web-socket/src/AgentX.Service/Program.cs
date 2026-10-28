// Dummy "AI Agent X". Runs as a service on machine M and binds to the LOOPBACK
// interface only (127.0.0.1:5001). Nothing outside machine M can dial it:
// there is no inbound NAT/firewall rule, and loopback is not routable.
// The only way out is the outbound tunnel opened by AgentX.RelayListener.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenLocalhost(5001, o => o.UseHttps());
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", agent = "x", machine = Environment.MachineName }));

// Dummy tool catalogue the agent advertises.
app.MapGet("/get_ai_tools", () => Results.Ok(new
{
    agent = "agent-x",
    machine = Environment.MachineName,
    tools = new object[]
    {
        new { name = "summarize_repo",   description = "Summarizes a git repository", costUnits = 3 },
        new { name = "scan_logs",        description = "Scans local log files for anomalies", costUnits = 5 },
        new { name = "inspect_network",  description = "Reports local sockets and routes", costUnits = 1 },
        new { name = "run_unit_tests",   description = "Executes the local test suite", costUnits = 8 }
    }
}));

// Dummy inference endpoint.
app.MapGet("/ask", (string? q) => Results.Ok(new
{
    agent = "agent-x",
    machine = Environment.MachineName,
    question = q ?? "(empty)",
    answer = string.IsNullOrWhiteSpace(q)
        ? "Ask me something, e.g. ?q=which tools do you have"
        : $"Agent X processed '{q}' locally and suggests using the 'summarize_repo' tool.",
    answeredAtUtc = DateTimeOffset.UtcNow
}));

app.Run();
