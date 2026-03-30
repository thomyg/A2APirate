# AgentCard — The Business Card of an A2A Agent

## What is an AgentCard?

Every A2A agent exposes a JSON file at a well-known URL. This file fully describes the agent: who it is, what it can do, and how to reach it.

**URL:** `GET /.well-known/agent-card.json`

## Example: Our PirateSpecialist

```json
{
  "name": "PirateSpecialist",
  "description": "Answers all questions - but as a pirate! Arrr!",
  "version": "1.0.0",
  "supportedInterfaces": [
    {
      "url": "http://localhost:5100/a2a/pirate",
      "protocolBinding": "JSONRPC",
      "protocolVersion": "1.0"
    }
  ],
  "defaultInputModes": ["text/plain"],
  "defaultOutputModes": ["text/plain"],
  "capabilities": {
    "streaming": true,
    "pushNotifications": false
  },
  "skills": [
    {
      "id": "pirate-speak",
      "name": "Pirate Speech",
      "description": "Answers any question in pirate style",
      "tags": ["pirate", "fun", "persona"]
    }
  ]
}
```

## The Individual Fields Explained

### Identity
- **name**: Unique name of the agent
- **description**: What the agent does (used for discovery and routing)
- **version**: Semantic Versioning — important for compatibility

### Interface (how do I reach the agent?)
- **url**: The actual endpoint URL for JSON-RPC requests
- **protocolBinding**: Always "JSONRPC" in the A2A protocol
- **protocolVersion**: Version of the A2A protocol

### Capabilities (what can the agent do?)
- **streaming**: Does the agent support SSE streaming? (SendStreamingMessage)
- **pushNotifications**: Can the agent send push notifications?

### Skills (what are the agent's abilities?)
- **id**: Unique ID of the skill
- **name**: Human-readable name
- **description**: Description for LLM-based routing
- **tags**: For filtering and categorization

## Why is the AgentCard So Important?

1. **Discovery-first:** An orchestrator can discover hundreds of agents and select the right one
2. **Self-documenting:** No separate API docs needed
3. **LLM-routable:** The descriptions are written so that an LLM can decide which agent fits which request
4. **No AgentCard = no A2A:** Discovery is fundamental, not optional
