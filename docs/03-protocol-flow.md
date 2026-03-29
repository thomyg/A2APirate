# A2A Protocol Flow — HTTP Step by Step

## Overview

```
Orchestrator                                    PirateSpecialist
     |                                                |
     |  1. GET /.well-known/agent-card.json           |
     | ---------------------------------------------> |
     | <--------------------------------------------- |
     |     200 OK { name, capabilities, url, ... }    |
     |                                                |
     |  2. POST /a2a/pirate                           |
     |     { method: "message/sendStream", ... }      |
     | ---------------------------------------------> |
     |                                                |
     | <== SSE Event: task (Submitted) ============== |
     | <== SSE Event: status (Working) ============== |
     | <== SSE Event: artifact ("Arrr") ============= |
     | <== SSE Event: artifact ("! I") ============== |
     | <== SSE Event: artifact (" am") ============== |
     | <== SSE Event: artifact ("...") ============== |
     | <== SSE Event: status (Completed) ============ |
     |                                                |
```

## Phase 1: Discovery

```http
GET http://localhost:5100/.well-known/agent-card.json
Accept: application/json
```

**What happens:** The orchestrator fetches the AgentCard. It learns:
- The agent is called "PirateSpecialist"
- It supports streaming
- Its endpoint is `http://localhost:5100/a2a/pirate`

## Phase 2: Sending a Message (Streaming)

```http
POST http://localhost:5100/a2a/pirate
Content-Type: application/json

{
    "jsonrpc": "2.0",
    "id": "req-001",
    "method": "message/sendStream",
    "params": {
        "message": {
            "role": "user",
            "messageId": "msg-001",
            "contextId": "conversation-42",
            "parts": [
                {
                    "kind": "text",
                    "text": "What is the best ship?"
                }
            ]
        }
    }
}
```

**Important details:**
- **jsonrpc: "2.0"**: A2A uses JSON-RPC 2.0 as its wire format
- **method: "message/sendStream"**: Tells the server that we want SSE streaming
- **contextId**: Groups the conversation together (same ID = same thread)
- **parts**: The actual message, can contain text, files, or structured data

## Phase 3: SSE Response

The response comes back as Server-Sent Events:

```
data: {"jsonrpc":"2.0","result":{"task":{"id":"t-123","status":{"state":"submitted"}}}}

data: {"jsonrpc":"2.0","result":{"statusUpdate":{"status":{"state":"working"}}}}

data: {"jsonrpc":"2.0","result":{"artifactUpdate":{"artifact":{"parts":[{"kind":"text","text":"Arrr"}]}}}}

data: {"jsonrpc":"2.0","result":{"artifactUpdate":{"artifact":{"parts":[{"kind":"text","text":"! The"}]}}}}

data: {"jsonrpc":"2.0","result":{"statusUpdate":{"status":{"state":"completed"}}}}
```

**Event types:**
| Event | Meaning |
|-------|---------|
| `task` | Task was created, contains initial task ID and status |
| `statusUpdate` | State change: submitted -> working -> completed |
| `artifactUpdate` | Content chunk (during streaming, tokens arrive one by one) |

## Task Lifecycle

```
submitted --> working --> completed
                    \--> failed
                    \--> inputRequired (agent needs more info)
                    \--> canceled
```

## contextId — Conversation Continuity

```
Message 1: contextId = "abc-123"  ->  "My name is Blackbeard"
Message 2: contextId = "abc-123"  ->  "What is my name?"
                                       Agent remembers: "Blackbeard!"

Message 3: contextId = "xyz-789"  ->  "What is my name?"
                                       New context, agent doesn't know
```

Same `contextId` = same conversation thread. The agent stores the chat history per contextId.
