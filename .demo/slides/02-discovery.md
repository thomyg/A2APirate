---
theme: default
layout: intro
transition: fadeIn
---

# Discovery in Action

Let's talk to an A2A agent — with nothing but HTTP.

---
layout: default
transition: slideRight
---

# The Discovery Flow

```
1. Client sends    GET /.well-known/agent-card.json
2. Server returns  { name, description, skills, url, capabilities }
3. Client now knows HOW to talk to this agent
4. Client sends    POST /a2a/pirate  (JSON-RPC, method: "SendMessage")
5. Server responds (sync or SSE stream via "SendStreamingMessage")
```

No SDK required. No special client. Just **HTTP + JSON**.

---
layout: default
transition: slideRight
---

# Multi-Agent Discovery

A host can serve **multiple agents** on different paths:

```
GET /agents  →  [ PirateSpecialistCard, DictionaryAgentCard ]
```

Each card has its own endpoint URL.
The orchestrator learns the full catalog in one call.

---
layout: section
transition: fadeIn
---

# You just spoke A2A.

No SDK. No framework. Just `curl` and JSON.
