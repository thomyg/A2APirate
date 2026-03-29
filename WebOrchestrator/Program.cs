using A2A;
using System.Text.Json;

// Load .env file from the solution root
LoadEnvFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

var specialistUrl = Environment.GetEnvironmentVariable("SPECIALIST_BASE_URL")
    ?? "http://localhost:5100";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

// ── API: Fetch the specialist's AgentCard ───────────────────────────────
app.MapGet("/api/agent-card", async () =>
{
    var resolver = new A2ACardResolver(new Uri(specialistUrl));
    var card = await resolver.GetAgentCardAsync();
    return Results.Ok(card);
});

// ── API: Stream chat via SSE to the browser ─────────────────────────────
// The browser calls this with a POST, and we stream back SSE events.
// This is the BFF (Backend For Frontend) that translates between
// simple HTTP SSE and the A2A JSON-RPC protocol.
app.MapPost("/api/chat", async (HttpContext http, ChatRequest req) =>
{
    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers.Connection = "keep-alive";

    var writer = http.Response.BodyWriter;
    var ct = http.RequestAborted;

    // Resolve the A2A endpoint from the AgentCard
    var resolver = new A2ACardResolver(new Uri(specialistUrl));
    var card = await resolver.GetAgentCardAsync();
    var endpointUrl = card.SupportedInterfaces?.FirstOrDefault()?.Url
        ?? throw new InvalidOperationException("AgentCard has no endpoint URL");

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

    // Send metadata event so the UI can show the outbound request details
    await WriteSse(http, ct, "request", new
    {
        method = "message/sendStream",
        endpoint = endpointUrl,
        messageId,
        contextId = req.ContextId,
        agentName = card.Name,
        text = req.Message
    });

    // Stream A2A events back to the browser as SSE
    var eventIndex = 0;
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

    // Final summary
    await WriteSse(http, ct, "done", new { totalEvents = eventIndex });
});

// Fallback: serve index.html for the root path
app.MapFallbackToFile("index.html");

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  🏴‍☠️  A2A Web Orchestrator");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Specialist  {specialistUrl}");
Console.WriteLine($"  Web UI      http://localhost:5200");
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

// ── Helper types ────────────────────────────────────────────────────────

record ChatRequest(string Message, string? ContextId);
