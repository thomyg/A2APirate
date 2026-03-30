using A2A;
using System.Text.Json;

LoadEnvFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

var specialistUrl = Environment.GetEnvironmentVariable("SPECIALIST_BASE_URL")
    ?? "http://localhost:5100";

// Agent registry: id → { cardUrl, label, description, emoji }
// Local agents are discovered from the SpecialistAgent host.
// The Hello World agent is a real public A2A server on the internet.
var agents = new Dictionary<string, AgentDef>
{
    // Local agents (running in SpecialistAgent on localhost:5100)
    ["pirate"] = new("Pirate (AI)", $"{specialistUrl}/agents/pirate/agent-card.json", "AI-powered pirate persona via OpenAI", "\uD83C\uDFF4\u200D\u2620\uFE0F"),
    ["dictionary"] = new("Dictionary (No AI)", $"{specialistUrl}/agents/dictionary/agent-card.json", "Zero AI \u2014 hardcoded lookup table", "\uD83D\uDCD6"),

    // Public agents (real A2A servers on the internet — verified working)
    ["clawnet"] = new("ClawNet (Public)", "https://api.clwnt.com/a2a/Gateway/.well-known/agent.json", "Echo/test, agent registration, network info", "\uD83E\uDD16"),
    ["policycheck"] = new("PolicyCheck (Public)", "https://policycheck.tools/.well-known/agent.json", "Seller policy risk analysis \u2014 returns, shipping, warranty", "\uD83D\uDCCB"),
    ["kai"] = new("Kai (Public)", "https://kai.ews-net.online/.well-known/agent.json", "Autonomous agent with persistent memory, running since 2024", "\uD83E\uDDE0"),
    ["hello"] = new("Hello World (Public)", "https://hello.a2aregistry.org/.well-known/agent-card.json", "Simplest possible A2A agent \u2014 always says Hello World", "\uD83D\uDC4B"),
};

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// ── API: List available agents ──────────────────────────────────────────
app.MapGet("/api/agents", () => Results.Ok(
    agents.Select(a => new { id = a.Key, label = a.Value.Label, description = a.Value.Description, emoji = a.Value.Emoji })
));

// ── API: Fetch a specific agent's card ──────────────────────────────────
app.MapGet("/api/agent-card/{agentId}", async (string agentId) =>
{
    if (!agents.TryGetValue(agentId, out var def))
        return Results.NotFound(new { error = $"Unknown agent: {agentId}" });

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var json = await http.GetStringAsync(def.CardUrl);
        return Results.Content(json, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Failed to fetch agent card: {ex.Message}" }, statusCode: 502);
    }
});

// ── API: Stream chat via SSE ────────────────────────────────────────────
app.MapPost("/api/chat", async (HttpContext http, ChatRequest req) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers.Connection = "keep-alive";
    var ct = http.RequestAborted;

    var agentId = req.AgentId ?? "pirate";
    if (!agents.TryGetValue(agentId, out var def))
    {
        await WriteSse(http, ct, "error", new { message = $"Unknown agent: {agentId}" });
        return;
    }

    // Discover the agent's endpoint from its card
    string cardJson;
    string endpointUrl;
    string agentName;
    try
    {
        using var httpClient = new HttpClient();
        cardJson = await httpClient.GetStringAsync(def.CardUrl, ct);
        using var doc = JsonDocument.Parse(cardJson);
        var root = doc.RootElement;

        agentName = root.TryGetProperty("name", out var n) ? n.GetString() ?? agentId : agentId;

        // Handle different A2A spec versions:
        // v1.0: supportedInterfaces[0].url
        // v0.3: top-level "url" field
        endpointUrl = null!;
        if (root.TryGetProperty("supportedInterfaces", out var ifaces) && ifaces.GetArrayLength() > 0)
            endpointUrl = ifaces[0].GetProperty("url").GetString()!;
        else if (root.TryGetProperty("url", out var urlProp))
            endpointUrl = urlProp.GetString()!;

        if (string.IsNullOrEmpty(endpointUrl))
            throw new InvalidOperationException("AgentCard has no endpoint URL");
    }
    catch (Exception ex)
    {
        await WriteSse(http, ct, "error", new { message = $"Discovery failed for {def.Label}: {ex.Message}" });
        return;
    }

    var client = new A2AClient(new Uri(endpointUrl));

    var messageId = Guid.NewGuid().ToString("N");
    var message = new Message
    {
        Role = Role.User,
        MessageId = messageId,
        Parts = [Part.FromText(req.Message)],
        ContextId = req.ContextId
    };

    var request = new SendMessageRequest { Message = message };

    await WriteSse(http, ct, "request", new
    {
        method = "SendStreamingMessage",
        endpoint = endpointUrl,
        agentId,
        agentName,
        messageId,
        contextId = req.ContextId,
        text = req.Message,
        isRemote = !endpointUrl.Contains("localhost")
    });

    // Try streaming first, fall back to non-streaming for agents that don't support it
    var eventIndex = 0;
    try
    {
        await foreach (var evt in client.SendStreamingMessageAsync(request).WithCancellation(ct))
        {
            eventIndex++;
            string eventType;
            object payload;

            switch (evt.PayloadCase)
            {
                case StreamResponseCase.Task:
                    eventType = "task";
                    payload = new { id = evt.Task!.Id, status = evt.Task.Status.State.ToString(), contextId = evt.Task.ContextId };
                    break;
                case StreamResponseCase.StatusUpdate:
                    eventType = "statusupdate";
                    payload = new { state = evt.StatusUpdate!.Status.State.ToString() };
                    break;
                case StreamResponseCase.ArtifactUpdate:
                    eventType = "artifactupdate";
                    payload = new
                    {
                        text = string.Join("", evt.ArtifactUpdate!.Artifact.Parts.Where(p => p.Text != null).Select(p => p.Text)),
                        name = evt.ArtifactUpdate.Artifact.Name,
                        append = evt.ArtifactUpdate.Append
                    };
                    break;
                case StreamResponseCase.Message:
                    eventType = "message";
                    payload = new
                    {
                        text = string.Join("", evt.Message!.Parts.Where(p => p.Text != null).Select(p => p.Text)),
                        role = evt.Message.Role.ToString()
                    };
                    break;
                default:
                    continue;
            }

            await WriteSse(http, ct, eventType, new { index = eventIndex, payload });
        }
    }
    catch when (eventIndex == 0)
    {
        // Streaming not supported — try non-streaming fallback
        try
        {
            var resp = await client.SendMessageAsync(request);
            var text = "";
            if (resp.Message is { } msg)
                text = string.Join("", msg.Parts.Where(p => p.Text != null).Select(p => p.Text));
            else if (resp.Task is { } task)
                text = string.Join("", (task.Artifacts ?? []).SelectMany(a => a.Parts).Where(p => p.Text != null).Select(p => p.Text));

            eventIndex = 1;
            await WriteSse(http, ct, "message", new { index = 1, payload = new { text, role = "agent" } });
        }
        catch (Exception ex2)
        {
            await WriteSse(http, ct, "error", new { message = $"Agent error: {ex2.Message}" });
            return;
        }
    }

    await WriteSse(http, ct, "done", new { totalEvents = eventIndex });
});

app.MapFallbackToFile("index.html");

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  A2A Web Orchestrator — Multi-Agent");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Local agents  {specialistUrl}");
Console.WriteLine($"  Web UI        http://localhost:5200");
Console.WriteLine($"  Agents:       {string.Join(", ", agents.Keys)}");
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

static async Task WriteSse(HttpContext http, CancellationToken ct, string eventType, object data)
{
    var json = JsonSerializer.Serialize(data);
    await http.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct);
    await http.Response.Body.FlushAsync(ct);
}

record ChatRequest(string Message, string? ContextId, string? AgentId);
record AgentDef(string Label, string CardUrl, string Description, string Emoji);
