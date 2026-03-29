using OrchestratorAgent.Services;

// Load .env file from the solution root (one level up from the project folder).
LoadEnvFile(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"));

// =============================================================================
// LEARNING POINT: OrchestratorAgent — the "Boss"
//
// This console agent does the following:
// 1. Discovers the PirateSpecialist via AgentCard Discovery
// 2. Accepts user input from the console
// 3. Delegates the request to the specialist via A2A protocol
// 4. Streams the response token by token to the console
//
// In a real system, the orchestrator would:
// - Know about multiple specialists
// - Use an LLM to decide which specialist handles the request
// - Combine responses and return them to the user
//
// For this learning example: always delegate to the pirate.
// =============================================================================

// --verbose flag: shows raw HTTP/SSE details
var verbose = args.Contains("--verbose");

// LEARNING POINT: contextId = conversation bracket.
// All messages with the same contextId belong to the same conversation.
// This allows the specialist to remember previous messages.
var contextId = Guid.NewGuid().ToString();

// Specialist URL (default: localhost:5100, configurable via env variable)
var specialistUrl = Environment.GetEnvironmentVariable("SPECIALIST_BASE_URL")
    ?? "http://localhost:5100";

var proxy = new SpecialistProxy(specialistUrl);

// ── Banner ──────────────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  ⚓  A2A Orchestrator");
Console.ResetColor();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Specialist  {specialistUrl}");
if (verbose) Console.WriteLine("  Verbose     ON");
Console.ResetColor();
Console.WriteLine();

// ── Step 1: Discovery — fetch AgentCard ─────────────────────────────────
try
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Discovering agent... ");
    Console.ResetColor();

    var card = await proxy.DiscoverAsync();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("OK");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Name        {card.Name}");
    Console.WriteLine($"  Skills      {string.Join(", ", card.Skills?.Select(s => s.Name) ?? [])}");
    Console.WriteLine($"  Streaming   {card.Capabilities?.Streaming}");
    Console.ResetColor();
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
    Console.Write("  You ▸ ");
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
        // LEARNING POINT: This is where the actual A2A communication happens.
        // SendStreamingAsync() sends the user text via JSON-RPC
        // and yields the response chunks as IAsyncEnumerable.
        // We write each token immediately to the console -> live streaming!
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  🏴‍☠️  ▸ ");

        await foreach (var chunk in proxy.SendStreamingAsync(input, contextId))
        {
            Console.Write(chunk);
        }

        Console.ResetColor();
        Console.WriteLine();

        // Verbose: print SSE summary AFTER the response (no interleaving)
        if (verbose && proxy.LastSummary is { } summary)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    ── {summary.TotalEvents} SSE events ({summary.ArtifactEvents} chunks) in {summary.Duration.TotalMilliseconds:F0}ms | status: {summary.LastStatus}");
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
Console.WriteLine("  ⚓  Arrr, until next time!");
Console.ResetColor();
Console.WriteLine();

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
