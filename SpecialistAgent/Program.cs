using A2A;
using A2A.AspNetCore;
using SpecialistAgent.Agents;

// =============================================================================
// LEARNING POINT: SpecialistAgent Startup
//
// This ASP.NET Core server does three things:
// 1. Registers the PirateSpecialist as an A2A agent in the DI container
// 2. Exposes POST /a2a/pirate as a JSON-RPC endpoint (message:send, message:stream)
// 3. Exposes GET /.well-known/agent-card.json (AgentCard Discovery)
//
// That is all an A2A-compatible agent needs!
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// Swagger for manual testing (optional, but helpful for learning)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// LEARNING POINT: AddA2AAgent<T>() registers:
//   - The agent handler (PirateSpecialist) as a Scoped Service
//   - A2AServer: processes incoming JSON-RPC requests
//   - InMemoryTaskStore: stores task state for ongoing conversations
//   - AgentCard: becomes available in the DI container for MapWellKnownAgentCard()
var agentUrl = "http://localhost:5100/a2a/pirate";
builder.Services.AddA2AAgent<PirateSpecialist>(
    PirateSpecialist.GetAgentCard(agentUrl));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// LEARNING POINT: MapA2A() registers a single POST endpoint.
// Internally, A2AJsonRpcProcessor dispatches based on the JSON-RPC "method":
//   - "message/send"       -> Synchronous response (JSON)
//   - "message/sendStream" -> SSE stream (Server-Sent Events)
//   - "tasks/get"          -> Query task status
//   - "tasks/cancel"       -> Cancel task
app.MapA2A("/a2a/pirate");

// LEARNING POINT: MapWellKnownAgentCard() registers GET /.well-known/agent-card.json
// This is the discovery endpoint. Other agents find us through this.
// Without an AgentCard = no A2A. Discovery is fundamental, not optional.
var agentCard = app.Services.GetRequiredService<AgentCard>();
app.MapWellKnownAgentCard(agentCard);

// Health check endpoint (simple smoke test)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", agent = "PirateSpecialist" }));

Console.WriteLine("=== PirateSpecialist Agent ===");
Console.WriteLine($"A2A Endpoint:    POST {agentUrl}");
Console.WriteLine($"AgentCard:       GET  http://localhost:5100/.well-known/agent-card.json");
Console.WriteLine($"Health:          GET  http://localhost:5100/health");
Console.WriteLine($"Swagger:         GET  http://localhost:5100/swagger");
Console.WriteLine("Waiting for messages... Arrr!");
Console.WriteLine();

app.Run();
