using System.Net.Http.Json;
using A2A;

namespace OrchestratorAgent.Services;

/// <summary>
/// KEY CONCEPT: The AgentRouter decides WHICH agent handles a user message.
///
/// This is the core orchestration pattern in multi-agent systems.
/// Two routing strategies are supported:
///
///   1. KEYWORD routing (no AI):
///      Matches the user's input against each agent's skills, tags, name, and description.
///      Pure string matching — no API key needed, no latency, fully deterministic.
///      This proves that A2A orchestration is just a protocol concern, not an AI concern.
///
///   2. AI routing:
///      Sends the user's input + a description of all available agents to an LLM.
///      The LLM picks the best agent. More flexible, handles ambiguous inputs,
///      but requires an API key and adds latency.
///
/// The routing mode is configured via ROUTING_MODE environment variable.
/// Default: "keyword" — because A2A is a protocol, AI is optional.
/// </summary>
public sealed class AgentRouter
{
    private readonly List<AgentEntry> _agents = [];
    private readonly RoutingMode _mode;
    private readonly bool _verbose;

    public IReadOnlyList<AgentEntry> Agents => _agents;

    public AgentRouter(RoutingMode mode, bool verbose = false)
    {
        _mode = mode;
        _verbose = verbose;
    }

    /// <summary>
    /// KEY CONCEPT: Discovers ALL agents from the host's /agents endpoint.
    ///
    /// Unlike single-agent discovery (GET /.well-known/agent-card.json),
    /// the /agents endpoint returns an array of AgentCards — one per hosted agent.
    /// This is how a multi-agent host advertises its full catalog.
    ///
    /// Each discovered agent gets its own SpecialistProxy, ready to send messages.
    /// </summary>
    public async Task<int> DiscoverAgentsAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var cards = await http.GetFromJsonAsync<AgentCard[]>("/agents")
            ?? throw new InvalidOperationException("GET /agents returned null");

        foreach (var card in cards)
        {
            var endpointUrl = card.SupportedInterfaces?.FirstOrDefault()?.Url;
            if (endpointUrl == null) continue;

            var proxy = new SpecialistProxy(endpointUrl, alreadyDiscovered: card);
            _agents.Add(new AgentEntry(card, proxy));
        }

        return _agents.Count;
    }

    /// <summary>
    /// Routes a user message to the best agent.
    /// Returns the AgentEntry that should handle the message, or null if no match.
    /// </summary>
    public async Task<RoutingResult> RouteAsync(string userText)
    {
        if (_agents.Count == 0)
            return new RoutingResult(null, "No agents available");

        if (_agents.Count == 1)
            return new RoutingResult(_agents[0], "Only one agent available");

        return _mode switch
        {
            RoutingMode.Keyword => RouteByKeyword(userText),
            RoutingMode.AI => await RouteByAIAsync(userText),
            _ => RouteByKeyword(userText),
        };
    }

    // ── Keyword Routing (no AI) ────────────────────────────────────────────

    /// <summary>
    /// KEY CONCEPT: Keyword routing scores each agent by how many of its
    /// skills, tags, name, and description words appear in the user's input.
    ///
    /// This is intentionally simple. It proves that agent routing can work
    /// without any AI at all — it's just string matching against AgentCard metadata.
    /// The AgentCard IS the routing table.
    /// </summary>
    private RoutingResult RouteByKeyword(string userText)
    {
        var input = userText.ToLowerInvariant();
        var bestScore = 0;
        AgentEntry? bestAgent = null;

        foreach (var entry in _agents)
        {
            var score = ScoreAgent(input, entry.Card);

            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    [router] {entry.Card.Name}: score={score}");
                Console.ResetColor();
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestAgent = entry;
            }
        }

        if (bestAgent == null)
        {
            // No keyword match — fall back to first agent
            bestAgent = _agents[0];
            return new RoutingResult(bestAgent, $"No keyword match, defaulting to {bestAgent.Card.Name}");
        }

        return new RoutingResult(bestAgent, $"Keyword match (score: {bestScore})");
    }

    private static int ScoreAgent(string input, AgentCard card)
    {
        var score = 0;

        // Match against agent name
        if (card.Name != null && input.Contains(card.Name.ToLowerInvariant()))
            score += 10;

        // Match against skills
        if (card.Skills != null)
        {
            foreach (var skill in card.Skills)
            {
                // Skill name
                if (skill.Name != null && input.Contains(skill.Name.ToLowerInvariant()))
                    score += 5;

                // Skill tags — these are the routing keywords
                if (skill.Tags != null)
                {
                    foreach (var tag in skill.Tags)
                    {
                        if (input.Contains(tag.ToLowerInvariant()))
                            score += 3;
                    }
                }

                // Words in skill description
                if (skill.Description != null)
                {
                    var descWords = skill.Description.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in descWords)
                    {
                        if (word.Length > 3 && input.Contains(word))
                            score += 1;
                    }
                }
            }
        }

        // Match against agent description
        if (card.Description != null)
        {
            var descWords = card.Description.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in descWords)
            {
                if (word.Length > 3 && input.Contains(word))
                    score += 1;
            }
        }

        return score;
    }

    // ── AI Routing ─────────────────────────────────────────────────────────

    /// <summary>
    /// KEY CONCEPT: AI routing uses an LLM to classify user intent.
    ///
    /// The LLM receives a list of available agents (from their AgentCards)
    /// and the user's message, then returns the agent ID that best matches.
    ///
    /// This is more flexible than keyword routing — it handles:
    /// - Ambiguous requests ("tell me something fun")
    /// - Indirect references ("I need protocol help" → DictionaryAgent)
    /// - Multi-language input
    ///
    /// But it requires an API key, costs tokens, and adds latency.
    /// The educational message: AI makes routing BETTER, but the protocol works without it.
    /// </summary>
    private async Task<RoutingResult> RouteByAIAsync(string userText)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("    [router] No OPENAI_API_KEY — falling back to keyword routing");
                Console.ResetColor();
            }
            return RouteByKeyword(userText);
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");

        // Build the agent catalog for the LLM prompt
        var agentDescriptions = string.Join("\n", _agents.Select((a, i) =>
        {
            var skills = a.Card.Skills != null
                ? string.Join(", ", a.Card.Skills.Select(s => s.Name))
                : "none";
            var tags = a.Card.Skills != null
                ? string.Join(", ", a.Card.Skills.SelectMany(s => s.Tags ?? []))
                : "none";
            return $"  ID: {i}\n  Name: {a.Card.Name}\n  Description: {a.Card.Description}\n  Skills: {skills}\n  Tags: {tags}";
        }));

        var systemPrompt = $"""
            You are an agent router. Your job is to pick the best agent for the user's message.

            Available agents:
            {agentDescriptions}

            Rules:
            - Respond with ONLY the agent ID number (e.g. "0" or "1"). Nothing else.
            - If the user asks about protocols, technical concepts, or definitions, prefer the agent with educational/dictionary tags.
            - If the user wants creative, fun, or persona-based responses, prefer the agent with persona/fun tags.
            - If unsure, pick the agent whose description best matches.
            """;

        try
        {
            // Use the OpenAI ChatClient directly for the routing decision
            var clientOptions = new OpenAI.OpenAIClientOptions();
            if (!string.IsNullOrEmpty(baseUrl))
                clientOptions.Endpoint = new Uri(baseUrl);

            var client = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                clientOptions);

            var chatClient = client.GetChatClient(model);

            var messages = new List<OpenAI.Chat.ChatMessage>
            {
                OpenAI.Chat.ChatMessage.CreateSystemMessage(systemPrompt),
                OpenAI.Chat.ChatMessage.CreateUserMessage(userText),
            };

            var completion = await chatClient.CompleteChatAsync(messages);
            var response = completion.Value.Content[0].Text?.Trim() ?? "";

            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    [router] AI response: \"{response}\"");
                Console.ResetColor();
            }

            if (int.TryParse(response, out var agentIndex) && agentIndex >= 0 && agentIndex < _agents.Count)
            {
                return new RoutingResult(_agents[agentIndex], $"AI routed to {_agents[agentIndex].Card.Name}");
            }

            // LLM returned something unexpected — fall back to keyword
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    [router] AI returned unexpected value, falling back to keyword");
                Console.ResetColor();
            }
            return RouteByKeyword(userText);
        }
        catch (Exception ex)
        {
            if (_verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    [router] AI routing failed: {ex.Message} — falling back to keyword");
                Console.ResetColor();
            }
            return RouteByKeyword(userText);
        }
    }
}

/// <summary>
/// Wraps an AgentCard with its corresponding SpecialistProxy, ready to send messages.
/// </summary>
public sealed record AgentEntry(AgentCard Card, SpecialistProxy Proxy);

/// <summary>
/// The result of a routing decision: which agent was picked and why.
/// </summary>
public sealed record RoutingResult(AgentEntry? Agent, string Reason);

/// <summary>
/// keyword = rule-based routing (no AI, no API key, deterministic)
/// ai      = LLM-based intent classification (flexible, needs API key)
/// </summary>
public enum RoutingMode
{
    Keyword,
    AI,
}
