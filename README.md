# A2A Learning Sample — Pirate Agent

A minimal but complete example of **Agent-to-Agent (A2A) communication** using the Microsoft Agent Framework.

Two .NET processes that talk to each other via the A2A protocol:

```
User Input (Console)
    |
    v
┌─────────────────────┐         A2A Protocol (HTTP/SSE)        ┌────────────────────────┐
│  OrchestratorAgent   │  ──── POST /a2a/pirate ────────────►  │   SpecialistAgent      │
│  (Console App)       │  ◄─── Server-Sent Events (stream) ──  │   (ASP.NET Core)       │
│                      │                                        │                        │
│  "I am the boss"     │         AgentCard Discovery            │  "I am the pirate"     │
│  Receives user       │  ──── GET /.well-known/agent-card ──► │  Arrr!                 │
│  requests, delegates │                                        │                        │
└──────────────────────┘                                        └────────────────────────┘
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- OpenAI API Key (or GitHub Models Token)

## Setup

```bash
# 1. Clone the repo and set the API key
cp .env.example .env
# Edit .env and enter your OPENAI_API_KEY

# 2. Load environment variables (or set them manually)
export OPENAI_API_KEY=sk-...
export OPENAI_MODEL=gpt-4o-mini
```

## Try it yourself

```bash
# Terminal 1: Start the specialist
cd SpecialistAgent && dotnet run

# Terminal 2: Start the orchestrator
cd OrchestratorAgent && dotnet run

# What happens now?
# 1. Orchestrator fetches http://localhost:5100/.well-known/agent-card.json
# 2. You enter text
# 3. Orchestrator sends POST /a2a/pirate (method: message/sendStream)
# 4. Specialist responds as a pirate, streamed via SSE
# 5. Orchestrator prints the response token by token
```

### Verbose Mode (make the protocol visible)

```bash
cd OrchestratorAgent && dotnet run -- --verbose
```

Shows raw SSE events and HTTP details.

### Commands in the Orchestrator

| Command | Effect |
|---------|--------|
| `exit` | Exits the orchestrator |
| `reset` | Starts a new conversation (new contextId) |

## Testing Without the Orchestrator

The specialist has a `requests.http` file for direct testing:

```bash
# Fetch the AgentCard
curl http://localhost:5100/.well-known/agent-card.json | jq

# Send a message (JSON-RPC)
curl -X POST http://localhost:5100/a2a/pirate \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "message/send",
    "params": {
      "message": {
        "role": "user",
        "messageId": "m1",
        "parts": [{"kind": "text", "text": "Ahoy!"}]
      }
    }
  }'
```

## Project Structure

```
├── Claude.md                          <- Project spec for Claude Code
├── .env.example                       <- Template for API keys
├── a2a-learning.sln
│
├── SpecialistAgent/                   <- Agent B: the pirate (ASP.NET Core)
│   ├── Program.cs                     <- Startup, MapA2A(), AgentCard
│   ├── Agents/
│   │   └── PirateSpecialist.cs        <- IAgentHandler + AIAgent
│   └── requests.http                  <- HTTP test file
│
├── OrchestratorAgent/                 <- Agent A: the boss (Console App)
│   ├── Program.cs                     <- Main loop, Discovery, Streaming
│   └── Services/
│       └── SpecialistProxy.cs         <- A2A client wrapper
│
└── docs/
    ├── 01-what-is-a2a.md             <- What is A2A?
    ├── 02-agent-card.md              <- AgentCard explained
    └── 03-protocol-flow.md           <- HTTP flow step by step
```

## Core Concepts (links to docs)

- [What is A2A?](docs/01-what-is-a2a.md)
- [AgentCard explained](docs/02-agent-card.md)
- [Protocol Flow step by step](docs/03-protocol-flow.md)

## Technology Stack

| Package | Purpose |
|---------|---------|
| `A2A` | A2A protocol core (client + server) |
| `A2A.AspNetCore` | ASP.NET Core integration (MapA2A, MapWellKnownAgentCard) |
| `Microsoft.Agents.AI` | AIAgent, RunAsync/RunStreamingAsync |
| `Microsoft.Agents.AI.OpenAI` | OpenAI integration, AsAIAgent() extension |

## References

- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [A2A Protocol Spec](https://google.github.io/A2A/)
- [Agent Framework Samples](https://github.com/microsoft/Agent-Framework-Samples)
