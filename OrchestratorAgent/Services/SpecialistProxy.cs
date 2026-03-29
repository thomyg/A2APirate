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

    /// <summary>
    /// After each streaming call, this holds a summary of SSE events
    /// that can be printed by the caller (for verbose mode).
    /// </summary>
    public StreamingSummary? LastSummary { get; private set; }

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

        var resolver = new A2ACardResolver(_baseUrl);
        _agentCard = await resolver.GetAgentCardAsync();

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
    ///
    /// Verbose info is NOT printed during streaming (to avoid interleaving).
    /// Instead it's collected in LastSummary for the caller to print afterwards.
    /// </summary>
    public async IAsyncEnumerable<string> SendStreamingAsync(
        string userText,
        string? contextId = null)
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

        // Collect SSE event info for verbose summary (printed AFTER streaming)
        var summary = new StreamingSummary { ContextId = contextId };
        var startTime = DateTime.UtcNow;

        // LEARNING POINT: SendStreamingMessageAsync returns an IAsyncEnumerable<StreamResponse>.
        // Each StreamResponse event has a PayloadCase indicating what it contains.
        await foreach (var evt in _client.SendStreamingMessageAsync(request))
        {
            summary.TotalEvents++;

            switch (evt.PayloadCase)
            {
                case StreamResponseCase.Task:
                    summary.TaskId ??= evt.Task!.Id;
                    break;

                case StreamResponseCase.StatusUpdate:
                    summary.LastStatus = evt.StatusUpdate!.Status.State.ToString();
                    break;

                case StreamResponseCase.ArtifactUpdate:
                    // LEARNING POINT: ArtifactUpdate contains the actual content chunks.
                    // During streaming, tokens arrive here one by one.
                    summary.ArtifactEvents++;
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

        summary.Duration = DateTime.UtcNow - startTime;
        LastSummary = summary;
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

/// <summary>
/// Summary of a streaming SSE session, printed after the response is complete.
/// </summary>
public sealed class StreamingSummary
{
    public string? ContextId { get; set; }
    public string? TaskId { get; set; }
    public string? LastStatus { get; set; }
    public int TotalEvents { get; set; }
    public int ArtifactEvents { get; set; }
    public TimeSpan Duration { get; set; }
}
