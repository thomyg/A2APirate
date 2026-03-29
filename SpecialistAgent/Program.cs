using A2A;
using A2A.AspNetCore;
using SpecialistAgent.Agents;

// Load .env file from the solution root
LoadEnvFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

// =============================================================================
// LEARNING POINT: This server hosts MULTIPLE A2A agents on different paths.
//
// Each agent has its own:
//   - IAgentHandler implementation (the brain)
//   - AgentCard (the discovery metadata)
//   - POST endpoint (the JSON-RPC receiver)
//
// The DictionaryAgent uses ZERO AI — just a C# Dictionary<string,string>.
// The PirateSpecialist uses an LLM via the Microsoft Agent Framework.
// Both speak the exact same A2A protocol.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register both agents in DI as named/keyed services
// We use the explicit MapA2A(handler, path) overload for multi-agent support
builder.Services.AddSingleton<PirateSpecialist>();
builder.Services.AddSingleton<DictionaryAgent>();
builder.Services.AddSingleton<InMemoryTaskStore>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// LEARNING POINT: Each agent gets its own A2AServer instance.
// A2AServer wraps an IAgentHandler and handles the JSON-RPC protocol.
// MapA2A(handler, path) maps it to a POST endpoint.

var taskStore = app.Services.GetRequiredService<InMemoryTaskStore>();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

// Agent 1: PirateSpecialist (AI-powered)
var pirateUrl = "http://localhost:5100/a2a/pirate";
var pirateCard = PirateSpecialist.GetAgentCard(pirateUrl);
var pirateServer = new A2AServer(
    app.Services.GetRequiredService<PirateSpecialist>(),
    taskStore,
    new ChannelEventNotifier(),
    loggerFactory.CreateLogger<A2AServer>(),
    new A2AServerOptions());
app.MapA2A(pirateServer, "/a2a/pirate");

// Agent 2: DictionaryAgent (zero AI)
var dictUrl = "http://localhost:5100/a2a/dictionary";
var dictCard = DictionaryAgent.GetAgentCard(dictUrl);
var dictServer = new A2AServer(
    app.Services.GetRequiredService<DictionaryAgent>(),
    new InMemoryTaskStore(),
    new ChannelEventNotifier(),
    loggerFactory.CreateLogger<A2AServer>(),
    new A2AServerOptions());
app.MapA2A(dictServer, "/a2a/dictionary");

// LEARNING POINT: Each agent gets its own well-known card endpoint.
// In production you might have a single discovery endpoint listing all agents,
// but for learning we keep them separate so you can see each card individually.
app.MapWellKnownAgentCard(pirateCard);

// Serve individual agent cards by name
app.MapGet("/agents/pirate/agent-card.json", () => Results.Ok(pirateCard));
app.MapGet("/agents/dictionary/agent-card.json", () => Results.Ok(dictCard));

// List all hosted agents (for the orchestrator to discover)
app.MapGet("/agents", () => Results.Ok(new[] { pirateCard, dictCard }));

// Health check
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    agents = new[] { "PirateSpecialist", "DictionaryAgent" }
}));

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  A2A Agent Host — 2 agents running");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine();
Console.WriteLine("  🏴‍☠️  PirateSpecialist (AI-powered)");
Console.WriteLine($"     POST {pirateUrl}");
Console.WriteLine($"     Card GET  http://localhost:5100/agents/pirate/agent-card.json");
Console.WriteLine();
Console.WriteLine("  📖 DictionaryAgent (zero AI)");
Console.WriteLine($"     POST {dictUrl}");
Console.WriteLine($"     Card GET  http://localhost:5100/agents/dictionary/agent-card.json");
Console.WriteLine();
Console.WriteLine("  All agents:  GET  http://localhost:5100/agents");
Console.WriteLine("  Health:      GET  http://localhost:5100/health");
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  Ready — waiting for messages!");
Console.ResetColor();
Console.WriteLine();

app.Run();

static void LoadEnvFile(string path)
{
    if (!File.Exists(path)) return;
    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var sep = trimmed.IndexOf('=');
        if (sep < 0) continue;
        var key = trimmed[..sep].Trim();
        var value = trimmed[(sep + 1)..].Trim();
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);
    }
}
