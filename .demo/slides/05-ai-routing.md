---
theme: default
layout: intro
transition: fadeIn
---

# AI Routing

When keywords aren't enough, let an LLM decide.

---
layout: default
transition: slideRight
---

# Keyword vs AI Routing

| | Keyword | AI |
|---|---|---|
| **Needs API key?** | No | Yes |
| **Latency** | ~0ms | ~500ms |
| **Cost** | Free | Tokens |
| **Handles ambiguity?** | No | Yes |
| **Deterministic?** | Yes | No |
| **"I'm feeling curious"** | Falls back to default | Routes to DictionaryAgent |

Same protocol. Same agents. Different **decision engine**.

---
layout: default
transition: slideRight
---

# How AI Routing Works

1. Build a **catalog** of all agents from their AgentCards
2. Send catalog + user message to an LLM
3. LLM returns the agent ID (just a number)
4. Route to that agent

If the LLM fails? **Fall back to keyword routing.**
If no API key? **Fall back to keyword routing.**

The protocol never breaks.

---
layout: section
transition: fadeIn
---

# The protocol stays the same.

Only the routing decision changes.

`ROUTING_MODE=keyword` or `ROUTING_MODE=ai` — your choice.
