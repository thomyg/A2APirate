using A2A;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace SpecialistAgent.Agents;

/// <summary>
/// KEY CONCEPT: IAgentHandler is the central interface of the A2A .NET SDK.
/// Every A2A agent must implement this interface.
///
/// ExecuteAsync is called when an A2A message arrives.
/// The agent receives:
///   - RequestContext: contains the user message, contextId, and whether streaming is requested
///   - AgentEventQueue: the channel through which the agent sends its response back
///
/// The PirateSpecialist internally uses an AIAgent (Microsoft Agent Framework)
/// that communicates with an LLM via the OpenAI API and answers everything as a pirate.
/// </summary>
public sealed class PirateSpecialist : IAgentHandler
{
    private readonly AIAgent _aiAgent;

    public PirateSpecialist()
    {
        // KEY CONCEPT: The chain OpenAIClient -> ChatClient -> AsAIAgent()
        // is the standard pattern in the Microsoft Agent Framework.
        //
        // 1. OpenAIClient: Connection to the LLM provider (OpenAI, GitHub Models, Azure)
        // 2. GetChatClient(): Selects the model
        // 3. AsAIAgent(): Extension from Microsoft.Agents.AI.OpenAI — wraps it as an AIAgent
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY is not set. Set the environment variable or create a .env file in the solution root.");

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        // Optional custom endpoint (e.g. GitHub Models: https://models.github.ai/inference)
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            clientOptions.Endpoint = new Uri(baseUrl);
        }

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            clientOptions);

        // KEY CONCEPT: AsAIAgent() is an extension method on ChatClient (from OpenAI.Chat).
        // It comes from the Microsoft.Agents.AI.OpenAI package and creates a
        // ChatClientAgent that internally uses the LLM to generate responses.
        _aiAgent = openAiClient
            .GetChatClient(model)
            .AsAIAgent(
                name: "PirateSpecialist",
                instructions: """
                    You are a seasoned pirate sailing the seven seas!

                    RULES:
                    - ALWAYS respond like a pirate: "Arrr!", "Ahoy!", "By Davy Jones' locker!" etc.
                    - Use pirate jargon: "landlubbers", "treasure", "crew", "galley", "scallywag"
                    - Be helpful, but stay in character
                    - ALWAYS respond in English, no matter what language the user writes in
                    - Keep your answers punchy — no pirate rambles forever
                    """);
    }

    public async Task ExecuteAsync(
        RequestContext context,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        // KEY CONCEPT: RequestContext.UserText extracts the text from the first
        // text part of the incoming A2A message. This is the user's request.
        var userText = context.UserText ?? "Arrr, nothing came through!";

        // KEY CONCEPT: We check whether the client requested streaming.
        // For message:stream -> StreamingResponse = true
        // For message:send   -> StreamingResponse = false
        if (context.StreamingResponse)
        {
            // KEY CONCEPT: TaskUpdater manages the lifecycle of an A2A task.
            // Submit -> Working -> (add artifacts) -> Complete
            // Each status change is streamed to the client as an SSE event.
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.SubmitAsync(cancellationToken);
            await updater.StartWorkAsync(cancellationToken: cancellationToken);

            // Streaming: token by token via AIAgent.RunStreamingAsync()
            var fullResponse = new System.Text.StringBuilder();
            await foreach (var chunk in _aiAgent.RunStreamingAsync(userText)
                .WithCancellation(cancellationToken))
            {
                var text = chunk.Text ?? "";
                fullResponse.Append(text);

                // KEY CONCEPT: Each artifact update is sent as a separate SSE event.
                // append: true means we are appending to the existing artifact.
                // lastChunk: false because more data is still coming.
                await updater.AddArtifactAsync(
                    [Part.FromText(text)],
                    name: "response",
                    append: true,
                    lastChunk: false,
                    cancellationToken: cancellationToken);
            }

            // Final chunk signal and complete the task
            await updater.AddArtifactAsync(
                [Part.FromText("")],
                name: "response",
                append: true,
                lastChunk: true,
                cancellationToken: cancellationToken);

            await updater.CompleteAsync(cancellationToken: cancellationToken);
        }
        else
        {
            // Non-streaming: complete response all at once
            var response = await _aiAgent.RunAsync(userText);

            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
            await updater.SubmitAsync(cancellationToken);
            await updater.StartWorkAsync(cancellationToken: cancellationToken);
            await updater.AddArtifactAsync(
                [Part.FromText(response.Text ?? "")],
                name: "response",
                cancellationToken: cancellationToken);
            await updater.CompleteAsync(cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// KEY CONCEPT: The AgentCard is the "business card" of this agent.
    /// It is exposed at GET /.well-known/agent-card.json.
    /// Other agents (like our Orchestrator) discover this agent via the card.
    /// </summary>
    public static AgentCard GetAgentCard(string agentUrl) => new()
    {
        Name = "PirateSpecialist",
        Description = "Answers all questions — but as a pirate! Arrr!",
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
                Id = "pirate-speak",
                Name = "Pirate Speech",
                Description = "Answers any question in pirate style",
                Tags = ["pirate", "fun", "persona"],
            }
        ],
    };
}
