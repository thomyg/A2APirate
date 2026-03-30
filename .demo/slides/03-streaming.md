---
theme: default
layout: intro
transition: fadeIn
---

# Streaming Token by Token

How A2A delivers responses via **Server-Sent Events**.

---
layout: default
transition: slideRight
---

# SSE — Server-Sent Events

- **Unidirectional**: server to client only
- **HTTP/1.1 compatible**: no WebSocket upgrade needed
- **Text-based**: each event is a `data: ...` line
- **Perfect for LLM streaming**: one token = one event

```
data: {"kind":"task","payload":{"status":"submitted"}}
data: {"kind":"statusUpdate","payload":{"status":"working"}}
data: {"kind":"artifactUpdate","payload":{"parts":[{"text":"Arr"}]}}
data: {"kind":"artifactUpdate","payload":{"parts":[{"text":"r!"}]}}
data: {"kind":"statusUpdate","payload":{"status":"completed"}}
```

---
layout: default
transition: slideRight
---

# Task Lifecycle

```
  Submitted ──► Working ──► Completed
                  │              │
                  │         (artifacts ready)
                  │
              ArtifactUpdate events
              (one per token/chunk)
```

The client receives status transitions AND content in the same stream.

---
layout: section
transition: fadeIn
---

# Same stream format.

Whether behind it is an LLM or a `Dictionary<string, string>`.
