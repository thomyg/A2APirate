using OrchestratorAgent.Services;

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

Console.WriteLine("=== A2A Orchestrator ===");
Console.WriteLine($"Specialist URL: {specialistUrl}");
Console.WriteLine($"Verbose:        {verbose}");
Console.WriteLine($"ContextId:      {contextId}");
Console.WriteLine();

// Step 1: Discovery — fetch AgentCard
try
{
    Console.WriteLine("Starting agent discovery...");
    Console.WriteLine();
    var card = await proxy.DiscoverAsync();
    Console.WriteLine();
    Console.WriteLine($"Connected to: {card.Name}");
    Console.WriteLine($"Skills: {string.Join(", ", card.Skills?.Select(s => s.Name) ?? [])}");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Discovery failed: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Is the SpecialistAgent running?");
    Console.WriteLine($"  cd SpecialistAgent && dotnet run");
    Console.ResetColor();
    return;
}

// Step 2: Main Loop — read user input, send to pirate, stream response
Console.WriteLine("Type something (or 'exit' to quit):");
Console.WriteLine(new string('-', 50));

while (true)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    // Create new contextId on "reset" command
    if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        contextId = Guid.NewGuid().ToString();
        Console.WriteLine($"[System] New conversation started. ContextId: {contextId}");
        continue;
    }

    try
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\nPirate: ");

        // LEARNING POINT: This is where the actual A2A communication happens.
        // SendStreamingAsync() sends the user text via JSON-RPC
        // and yields the response chunks as IAsyncEnumerable.
        // We write each token immediately to the console -> live streaming!
        await foreach (var chunk in proxy.SendStreamingAsync(input, contextId, verbose))
        {
            Console.Write(chunk);
        }

        Console.ResetColor();
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[Error] {ex.Message}");
        if (verbose)
        {
            Console.WriteLine(ex.StackTrace);
        }
        Console.ResetColor();
    }
}

Console.WriteLine("\nArrr, until next time!");
