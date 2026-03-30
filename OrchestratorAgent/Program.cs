using OrchestratorAgent.Services;

// Load .env file from the solution root (one level up from the project folder).
LoadEnvFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

// =============================================================================
// LEARNING POINT: OrchestratorAgent — the "Boss"
//
// This console agent demonstrates multi-agent routing via A2A:
// 1. Discovers ALL agents from the SpecialistAgent host (/agents endpoint)
// 2. Routes each user message to the best agent
// 3. Streams the response token by token
//
// KEY CONCEPT: Routing works in two modes:
//   --routing=keyword  (default) — rule-based, matches skills/tags, NO AI needed
//   --routing=ai                 — LLM classifies intent, picks the best agent
//
// This proves that A2A orchestration is a PROTOCOL concern.
// AI makes routing smarter, but the protocol works without it.
// =============================================================================

// Parse CLI flags
var verbose = args.Contains("--verbose");
var routingMode = ParseRoutingMode(args);

// Specialist URL (default: localhost:5100, configurable via env variable)
var specialistUrl = Environment.GetEnvironmentVariable("SPECIALIST_BASE_URL")
    ?? "http://localhost:5100";

// LEARNING POINT: contextId = conversation bracket.
// All messages with the same contextId belong to the same conversation.
var contextId = Guid.NewGuid().ToString();

// ── Banner ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  A2A Orchestrator — Multi-Agent Routing");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Host        {specialistUrl}");
Console.WriteLine($"  Routing     {routingMode.ToString().ToLowerInvariant()}{(routingMode == RoutingMode.Keyword ? " (no AI)" : " (LLM-based)")}");
if (verbose) Console.WriteLine("  Verbose     ON");
Console.ResetColor();
Console.WriteLine();

// ── Step 1: Discovery — fetch all AgentCards ────────────────────────────
var router = new AgentRouter(routingMode, verbose);

try
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Discovering agents... ");
    Console.ResetColor();

    var count = await router.DiscoverAgentsAsync(specialistUrl);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"found {count} agent{(count != 1 ? "s" : "")}");
    Console.ResetColor();

    // List discovered agents
    foreach (var entry in router.Agents)
    {
        var card = entry.Card;
        var tags = card.Skills?.SelectMany(s => s.Tags ?? []).ToArray() ?? [];
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  · {card.Name,-20} {card.Description}");
        if (tags.Length > 0)
            Console.WriteLine($"    {"",20} tags: {string.Join(", ", tags)}");
        Console.ResetColor();
    }
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("FAILED");
    Console.WriteLine();
    Console.WriteLine($"  {ex.Message}");
    Console.WriteLine();
    Console.ResetColor();
    Console.WriteLine("  Is the SpecialistAgent running?");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  cd SpecialistAgent && dotnet run");
    Console.ResetColor();
    return;
}

// ── Step 2: Chat loop ───────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Type a message, 'reset' for new conversation, 'exit' to quit.");
Console.ResetColor();
Console.WriteLine(new string('─', 60));

while (true)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("  You > ");
    Console.ResetColor();
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    // Create new contextId on "reset" command
    if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        contextId = Guid.NewGuid().ToString();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  New conversation started (contextId: {contextId[..8]}...)");
        Console.ResetColor();
        continue;
    }

    try
    {
        // LEARNING POINT: The router picks the best agent for this message.
        // In keyword mode, this is pure string matching — no AI, no API calls.
        // In AI mode, an LLM classifies the intent first.
        var result = await router.RouteAsync(input);

        if (result.Agent == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  No agent matched.");
            Console.ResetColor();
            continue;
        }

        // Show which agent was selected and why
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [{result.Agent.Card.Name}] {result.Reason}");
        Console.ResetColor();

        // Stream the response from the selected agent
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"  {result.Agent.Card.Name} > ");

        await foreach (var chunk in result.Agent.Proxy.SendStreamingAsync(input, contextId))
        {
            Console.Write(chunk);
        }

        Console.ResetColor();
        Console.WriteLine();

        // Verbose: print SSE summary AFTER the response (no interleaving)
        if (verbose && result.Agent.Proxy.LastSummary is { } summary)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    -- {summary.TotalEvents} SSE events ({summary.ArtifactEvents} chunks) in {summary.Duration.TotalMilliseconds:F0}ms | status: {summary.LastStatus}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n  Error: {ex.Message}");
        if (verbose) Console.WriteLine($"  {ex.StackTrace}");
        Console.ResetColor();
    }
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  Until next time!");
Console.ResetColor();
Console.WriteLine();

// ── Helpers ─────────────────────────────────────────────────────────────

static RoutingMode ParseRoutingMode(string[] args)
{
    // CLI flag: --routing=keyword or --routing=ai
    var flag = args.FirstOrDefault(a => a.StartsWith("--routing=", StringComparison.OrdinalIgnoreCase));
    if (flag != null)
    {
        var value = flag.Split('=', 2)[1];
        if (value.Equals("ai", StringComparison.OrdinalIgnoreCase))
            return RoutingMode.AI;
        return RoutingMode.Keyword;
    }

    // Environment variable: ROUTING_MODE=keyword|ai
    var env = Environment.GetEnvironmentVariable("ROUTING_MODE");
    if (env != null && env.Equals("ai", StringComparison.OrdinalIgnoreCase))
        return RoutingMode.AI;

    return RoutingMode.Keyword;
}

// Minimal .env file loader — reads KEY=VALUE pairs and sets them as environment variables.
static void LoadEnvFile(string path)
{
    if (!File.Exists(path)) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex < 0) continue;

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim();

        if (Environment.GetEnvironmentVariable(key) is null)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
