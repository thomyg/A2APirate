# A2A Learning Sample — Multi-Agent Routing

A minimal but complete example of **Agent-to-Agent (A2A) communication** using the Microsoft Agent Framework. Demonstrates multi-agent discovery, smart routing, and streaming — with and without AI.

```
User Input (Console or Web UI)
    |
    v
┌─────────────────────────┐       A2A Protocol (HTTP/SSE)       ┌────────────────────────┐
│  OrchestratorAgent      │                                      │   SpecialistAgent      │
│  (Console App)          │  --- GET /agents -----------------> │   (ASP.NET Core)       │
│                         │  <-- [PirateCard, DictCard] ------  │                        │
│  Routes user messages   │                                      │  Hosts multiple agents │
│  to the best agent:     │  --- POST /a2a/pirate ------------> │  on different paths:   │
│                         │  <-- SSE stream ------------------  │                        │
│  keyword = no AI        │                                      │  /a2a/pirate  (AI)     │
│  ai      = LLM routing  │  --- POST /a2a/dictionary -------> │  /a2a/dictionary (zero AI)│
│                         │  <-- SSE stream ------------------  │                        │
└─────────────────────────┘                                      └────────────────────────┘
         |
         v
┌─────────────────────────┐
│  WebOrchestrator        │  Browser-based Protocol Explorer
│  (ASP.NET Core + HTML)  │  with SSE inspector, AgentCard viewer,
│  http://localhost:5200  │  and multi-agent picker (local + public)
└─────────────────────────┘
```

## The Key Message

**A2A is a protocol, not an AI framework.** AI is optional — it makes agents smarter, but the protocol works without it.

This project proves it:
- **PirateSpecialist** uses an LLM (OpenAI/GitHub Models) to answer as a pirate
- **DictionaryAgent** uses zero AI — just a `Dictionary<string,string>` lookup table
- **Routing** works in keyword mode (string matching) or AI mode (LLM intent classification)
- All three use the **exact same A2A protocol**: AgentCard discovery, JSON-RPC, SSE streaming

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- OpenAI API Key or GitHub Models Token (only needed for PirateSpecialist and AI routing)

## Setup

```bash
cp .env.example .env
# Edit .env and enter your API key
```

## Try it yourself

### Console Orchestrator (multi-agent routing)

```bash
# Terminal 1: Start the specialist host (2 agents)
cd SpecialistAgent && dotnet run

# Terminal 2: Start the orchestrator
cd OrchestratorAgent && dotnet run
```

The orchestrator discovers all agents and routes messages by keyword matching:

```
  A2A Orchestrator — Multi-Agent Routing
  Host        http://localhost:5100
  Routing     keyword (no AI)

  Discovering agents... found 2 agents
  · PirateSpecialist      Answers all questions — but as a pirate! Arrr!
  · DictionaryAgent        A zero-AI agent that answers from a hardcoded lookup table.
```

### Routing Modes

```bash
# Keyword routing (default) — no AI, no API key needed
cd OrchestratorAgent && dotnet run

# AI routing — LLM classifies intent and picks the best agent
cd OrchestratorAgent && dotnet run -- --routing=ai

# Verbose mode — shows routing scores and SSE details
cd OrchestratorAgent && dotnet run -- --verbose

# Combine flags
cd OrchestratorAgent && dotnet run -- --routing=ai --verbose
```

You can also set `ROUTING_MODE=ai` in your `.env` file.

### Web Protocol Explorer

```bash
# Terminal 3: Start the web UI
cd WebOrchestrator && dotnet run
# Open http://localhost:5200
```

Features: agent picker (local + public agents), streaming chat, SSE event inspector, AgentCard viewer.

### Commands in the Orchestrator

| Command | Effect |
|---------|--------|
| `exit` | Exits the orchestrator |
| `reset` | Starts a new conversation (new contextId) |

## Testing Without the Orchestrator

```bash
# Fetch all agent cards
curl http://localhost:5100/agents | jq

# Fetch the PirateSpecialist card
curl http://localhost:5100/.well-known/agent-card.json | jq

# Send a message (JSON-RPC)
curl -X POST http://localhost:5100/a2a/pirate \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "SendMessage",
    "params": {
      "message": {
        "role": "user",
        "messageId": "m1",
        "parts": [{"kind": "text", "text": "Ahoy!"}]
      }
    }
  }'
```

See `SpecialistAgent/requests.http` for more examples.

## Project Structure

```
├── .env.example                       <- Template for API keys & routing config
├── a2a-learning.sln
│
├── SpecialistAgent/                   <- Agent host (ASP.NET Core, 2 agents)
│   ├── Program.cs                     <- Startup, MapA2A() for both agents
│   ├── Agents/
│   │   ├── PirateSpecialist.cs        <- AI-powered agent (LLM via OpenAI)
│   │   └── DictionaryAgent.cs         <- Zero-AI agent (hardcoded lookup)
│   └── requests.http                  <- HTTP test file
│
├── OrchestratorAgent/                 <- Multi-agent router (Console App)
│   ├── Program.cs                     <- Discovery, routing, streaming loop
│   └── Services/
│       ├── AgentRouter.cs             <- Keyword + AI routing strategies
│       └── SpecialistProxy.cs         <- A2A client wrapper per agent
│
├── WebOrchestrator/                   <- Browser-based protocol explorer
│   ├── Program.cs                     <- API backend (discovery, chat, SSE)
│   └── wwwroot/index.html             <- Single-page UI
│
└── docs/
    ├── 01-what-is-a2a.md             <- What is A2A?
    ├── 02-agent-card.md              <- AgentCard explained
    └── 03-protocol-flow.md           <- HTTP flow step by step
```

## Core Concepts

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
