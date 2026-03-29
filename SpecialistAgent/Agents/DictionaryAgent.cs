using A2A;

namespace SpecialistAgent.Agents;

/// <summary>
/// KEY CONCEPT: This agent uses ZERO AI. No LLM, no API keys, no external services.
///
/// It proves that the A2A protocol is transport-agnostic — the specialist behind the
/// endpoint can be anything: an LLM, a database lookup, a hardcoded dictionary, or
/// even a random number generator. The protocol doesn't care.
///
/// This agent does simple keyword matching against a fixed dictionary.
/// If no match is found, it returns a random funny "I don't know" response.
/// </summary>
public sealed class DictionaryAgent : IAgentHandler
{
    // Fixed knowledge base — no AI needed
    private static readonly Dictionary<string, string> Knowledge = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hello"] = "Hey there! I'm the Dictionary Agent. I don't use any AI — just a hardcoded lookup table. Try asking me about .NET, A2A, HTTP, or JSON!",
        ["hi"] = "Hello! I'm powered by a simple C# Dictionary<string, string> — no AI involved. Ask me about tech topics!",
        ["what is a2a"] = "A2A (Agent-to-Agent) is an open protocol for agent interoperability. It lets agents discover each other via AgentCards and communicate via JSON-RPC over HTTP. It was initiated by Google and supported by Microsoft, Salesforce, and others.",
        ["a2a"] = "A2A (Agent-to-Agent) is an open protocol that lets AI agents talk to each other over HTTP using JSON-RPC. Key concepts: AgentCard for discovery, message/send for sync, message/sendStream for SSE streaming.",
        [".net"] = ".NET is a free, open-source developer platform by Microsoft for building apps. This entire A2A demo is built with .NET 10, ASP.NET Core, and the A2A NuGet package.",
        ["dotnet"] = ".NET is Microsoft's cross-platform framework. Fun fact: this agent is literally a C# class with a Dictionary<string,string> — the simplest possible A2A agent implementation!",
        ["http"] = "HTTP (HyperText Transfer Protocol) is the foundation of A2A. Agents communicate via standard HTTP POST requests with JSON-RPC payloads. Streaming uses Server-Sent Events (SSE), which is HTTP/1.1 compatible.",
        ["json"] = "JSON-RPC 2.0 is the wire format for A2A. Every request has: jsonrpc (always '2.0'), method (like 'message/send'), params (the message), and id (for correlation).",
        ["json-rpc"] = "JSON-RPC 2.0 is A2A's wire format. A request looks like: {jsonrpc:'2.0', method:'message/send', params:{message:{...}}, id:'1'}. The response wraps the result in the same format.",
        ["sse"] = "Server-Sent Events (SSE) is how A2A does streaming. It's unidirectional (server to client), works over HTTP/1.1, and sends events as 'data: ...' lines. Each token from an LLM becomes one SSE event.",
        ["agentcard"] = "An AgentCard is a JSON file at /.well-known/agent-card.json that describes an agent: its name, capabilities, skills, and endpoint URL. It's the 'business card' of the A2A protocol — discovery happens here first.",
        ["agent card"] = "The AgentCard lives at GET /.well-known/agent-card.json. It tells other agents: who I am (name), what I can do (skills), how to reach me (URL), and what I support (streaming, push notifications).",
        ["contextid"] = "contextId is A2A's conversation threading mechanism. Same contextId = same conversation. The agent can use it to remember previous messages. New contextId = fresh start, agent forgets everything.",
        ["streaming"] = "A2A streaming uses method 'message/sendStream'. The response comes back as SSE events: first a Task (Submitted), then StatusUpdate (Working), then ArtifactUpdate events (one per token), and finally StatusUpdate (Completed).",
        ["task"] = "In A2A, a Task wraps a long-running response. It has a lifecycle: Submitted → Working → Completed (or Failed/Canceled). Tasks can have Artifacts (the actual content) and can be polled with tasks/get.",
        ["microsoft"] = "Microsoft supports A2A through the Agent Framework (Microsoft.Agents.AI) and the A2A .NET SDK. Their framework provides AIAgent, ChatClientAgent, and integration with OpenAI/Azure OpenAI.",
        ["google"] = "Google initiated the A2A protocol. It's designed to be vendor-neutral so agents from different providers can interoperate. The spec is at a2a-protocol.org.",
        ["mcp"] = "MCP (Model Context Protocol) by Anthropic is complementary to A2A. MCP = agent accesses tools/data. A2A = agent talks to agent. You can use both: A2A for inter-agent communication, MCP for tool access.",
        ["who are you"] = "I'm the Dictionary Agent — a zero-AI A2A agent. My entire intelligence is a C# Dictionary with ~20 entries. I exist to prove that A2A is just a protocol — the 'brain' behind it can be anything.",
        ["how do you work"] = "I'm incredibly simple: I split your message into words, check each against a Dictionary<string,string>, and return the first match. No AI, no ML, no neural networks — just string matching in C#.",
    };

    // Funny fallback responses when no match is found
    private static readonly string[] Fallbacks =
    [
        "I have exactly 20 things I know about, and that's not one of them. Try: a2a, http, json, sse, agentcard, streaming, task, .net, mcp",
        "My Dictionary<string,string> doesn't have an entry for that. I'm not AI — I'm literally a lookup table. Ask me about A2A protocol concepts!",
        "404 Knowledge Not Found. I'm a hardcoded dictionary, not ChatGPT. Try asking about: a2a, agentcard, contextid, streaming, json-rpc",
        "Hmm, that's not in my array. Fun fact: I have zero AI — this response was picked randomly from 8 pre-written strings.",
        "I don't know that one! But here's what I DO know: a2a, http, json, sse, agentcard, streaming, task, .net, mcp, contextid",
        "Beep boop — no match found. I'm proof that A2A agents don't need AI. My entire brain is 20 dictionary entries and this fallback array.",
        "That's above my pay grade. I'm the simplest possible A2A agent: a C# Dictionary + a Random fallback picker. Try asking about the A2A protocol!",
        "I wish I could help, but my entire knowledge fits in about 2KB of C# source code. Ask me about A2A, HTTP, JSON-RPC, or SSE instead!",
    ];

    private static readonly Random Rng = new();

    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var userText = context.UserText ?? "";

        // Simple keyword matching — check if any key appears in the user's input
        string? response = null;
        var lowerInput = userText.ToLowerInvariant();
        foreach (var (key, value) in Knowledge)
        {
            if (lowerInput.Contains(key.ToLowerInvariant()))
            {
                response = value;
                break;
            }
        }

        // No match → random funny fallback
        response ??= Fallbacks[Rng.Next(Fallbacks.Length)];

        // Return as a simple task-based response (same A2A flow as the pirate, just no AI)
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken);
        await updater.StartWorkAsync(cancellationToken: cancellationToken);

        if (context.StreamingResponse)
        {
            // Simulate streaming by splitting response into words
            // This shows the SSE streaming mechanism without any AI
            var words = response.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var chunk = (i > 0 ? " " : "") + words[i];
                await updater.AddArtifactAsync(
                    [Part.FromText(chunk)],
                    name: "response",
                    append: true,
                    lastChunk: false,
                    cancellationToken: cancellationToken);

                // Small delay to make streaming visible in the UI
                await Task.Delay(30, cancellationToken);
            }

            await updater.AddArtifactAsync(
                [Part.FromText("")],
                name: "response",
                append: true,
                lastChunk: true,
                cancellationToken: cancellationToken);
        }
        else
        {
            await updater.AddArtifactAsync(
                [Part.FromText(response)],
                name: "response",
                cancellationToken: cancellationToken);
        }

        await updater.CompleteAsync(cancellationToken: cancellationToken);
    }

    public static AgentCard GetAgentCard(string agentUrl) => new()
    {
        Name = "DictionaryAgent",
        Description = "A zero-AI agent that answers from a hardcoded lookup table. Proves A2A is just a protocol.",
        Version = "1.0.0",
        SupportedInterfaces =
        [
            new AgentInterface
            {
                Url = agentUrl,
                ProtocolBinding = "JSONRPC",
                ProtocolVersion = "1.0",
            }
        ],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Capabilities = new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = false,
        },
        Skills =
        [
            new AgentSkill
            {
                Id = "dictionary-lookup",
                Name = "Dictionary Lookup",
                Description = "Answers questions about A2A, HTTP, JSON-RPC, SSE, and other protocol concepts from a fixed dictionary. No AI involved.",
                Tags = ["no-ai", "dictionary", "a2a", "protocol", "educational"],
            }
        ],
    };
}
