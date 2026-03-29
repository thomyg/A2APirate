using A2A;

namespace OrchestratorAgent.Services;

/// <summary>
/// LEARNING POINT: The SpecialistProxy encapsulates all A2A client communication.
///
/// It does three things:
/// 1. Discovery: Retrieve the AgentCard from the specialist (GET /.well-known/agent-card.json)
/// 2. Send message: Send the user's text to the specialist
/// 3. Receive streaming: Receive SSE events token by token
///
/// The Orchestrator only knows the specialist's base URL.
/// Everything else (capabilities, endpoint URL) is learned from the AgentCard.
/// This is "discovery-first design".
/// </summary>
public sealed class SpecialistProxy
{
    private readonly Uri _baseUrl;
    private AgentCard? _agentCard;
    private A2AClient? _client;

    public SpecialistProxy(string baseUrl)
    {
        _baseUrl = new Uri(baseUrl);
    }

    /// <summary>
    /// LEARNING POINT: Discovery is the FIRST step in the A2A protocol.
    /// A2ACardResolver fetches GET {baseUrl}/.well-known/agent-card.json
    /// and deserializes the AgentCard.
    ///
    /// After that we know:
    /// - The agent's name and description
    /// - Whether it supports streaming
    /// - Which URL to reach it at (SupportedInterfaces[0].Url)
    /// </summary>
    public async Task<AgentCard> DiscoverAsync()
    {
        if (_agentCard != null) return _agentCard;

        Console.WriteLine($"[Discovery] Requesting AgentCard from: {_baseUrl}");

        var resolver = new A2ACardResolver(_baseUrl);
        _agentCard = await resolver.GetAgentCardAsync();

        Console.WriteLine($"[Discovery] Agent found: {_agentCard.Name}");
        Console.WriteLine($"[Discovery] Description:  {_agentCard.Description}");
        Console.WriteLine($"[Discovery] Streaming:    {_agentCard.Capabilities?.Streaming}");
        Console.WriteLine($"[Discovery] Endpoint:      {_agentCard.SupportedInterfaces?.FirstOrDefault()?.Url}");

        // Create A2AClient using the URL from the AgentCard
        var endpointUrl = _agentCard.SupportedInterfaces?.FirstOrDefault()?.Url
            ?? throw new InvalidOperationException("AgentCard has no SupportedInterfaces with a URL");
        _client = new A2AClient(new Uri(endpointUrl));

        return _agentCard;
    }

    /// <summary>
    /// LEARNING POINT: Sends a message and streams the response via SSE.
    ///
    /// SendStreamingMessageAsync() sends a JSON-RPC request with method "message/sendStream".
    /// The response arrives as an IAsyncEnumerable of StreamResponse events:
    ///   - Task: Task was created/updated (contains status)
    ///   - StatusUpdate: Status transition (Submitted -> Working -> Completed)
    ///   - ArtifactUpdate: New content (text chunks during streaming)
    ///   - Message: Direct message response (for non-task-based agents)
    /// </summary>
    public async IAsyncEnumerable<string> SendStreamingAsync(
        string userText,
        string? contextId = null,
        bool verbose = false)
    {
        if (_client == null)
            throw new InvalidOperationException("Call DiscoverAsync() first!");

        // LEARNING POINT: ContextId is set on the Message, not on the Configuration.
        // This is important: contextId is a message-level concept in the A2A protocol.
        var message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = [Part.FromText(userText)],
            ContextId = contextId
        };

        var request = new SendMessageRequest { Message = message };

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine($"[HTTP] POST (streaming)");
            Console.WriteLine($"[HTTP] Method: message/sendStream");
            Console.WriteLine($"[HTTP] ContextId: {contextId ?? "(new)"}");
            Console.WriteLine();
        }

        // LEARNING POINT: SendStreamingMessageAsync returns an IAsyncEnumerable<StreamResponse>.
        // Each StreamResponse event has a PayloadCase indicating what it contains.
        await foreach (var evt in _client.SendStreamingMessageAsync(request))
        {
            if (verbose)
            {
                Console.WriteLine($"  [SSE] PayloadCase={evt.PayloadCase}");
            }

            switch (evt.PayloadCase)
            {
                case StreamResponseCase.Task:
                    if (verbose)
                        Console.WriteLine($"  [SSE] Task ID={evt.Task!.Id}, Status={evt.Task.Status.State}");
                    break;

                case StreamResponseCase.StatusUpdate:
                    if (verbose)
                        Console.WriteLine($"  [SSE] Status -> {evt.StatusUpdate!.Status.State}");
                    break;

                case StreamResponseCase.ArtifactUpdate:
                    // LEARNING POINT: ArtifactUpdate contains the actual content chunks.
                    // During streaming, tokens arrive here one by one.
                    var artifact = evt.ArtifactUpdate!.Artifact;
                    foreach (var part in artifact.Parts)
                    {
                        if (part.Text != null)
                        {
                            yield return part.Text;
                        }
                    }
                    break;

                case StreamResponseCase.Message:
                    // Direct message response (not task-based)
                    foreach (var part in evt.Message!.Parts)
                    {
                        if (part.Text != null)
                        {
                            yield return part.Text;
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Non-streaming variant: sends and waits for the complete response.
    /// </summary>
    public async Task<string> SendAsync(string userText, string? contextId = null)
    {
        if (_client == null)
            throw new InvalidOperationException("Call DiscoverAsync() first!");

        var message = new Message
        {
            Role = Role.User,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = [Part.FromText(userText)],
            ContextId = contextId
        };

        var request = new SendMessageRequest { Message = message };

        var response = await _client.SendMessageAsync(request);

        // Response can arrive as a Message or as a Task
        if (response.Message is { } msg)
        {
            return string.Join("", msg.Parts.Where(p => p.Text != null).Select(p => p.Text));
        }

        if (response.Task is { } task)
        {
            // Extract text from the last artifact
            var artifacts = task.Artifacts ?? [];
            return string.Join("", artifacts
                .SelectMany(a => a.Parts)
                .Where(p => p.Text != null)
                .Select(p => p.Text));
        }

        return "[No response from specialist]";
    }
}
