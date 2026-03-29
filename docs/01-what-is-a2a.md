# What is A2A (Agent-to-Agent)?

## The Problem

Imagine you have 10 different AI agents. One can summarize documents, one can write code, one knows about product data. How do they talk to each other?

**Before:** Every vendor had its own protocol. Agent A from Microsoft couldn't talk to Agent B from Google. It's like phones that only work within a single carrier's network.

**Now with A2A:** An open standard initiated by Google and supported by Microsoft, Salesforce, and others. Any agent that speaks A2A can communicate with any other A2A agent — regardless of who built it.

## The Core Idea

A2A is **HTTP-based**. No magic, no proprietary binary protocol. Simply:

1. **Discovery:** Agent A asks "Who are you?" via `GET /.well-known/agent-card.json`
2. **Communication:** Agent A sends a message via `POST` with JSON-RPC
3. **Streaming:** Agent B responds via Server-Sent Events (SSE) — token by token

## A2A vs. MCP (Model Context Protocol)

| | A2A | MCP |
|---|---|---|
| **Purpose** | Agent talks to agent | Agent accesses tools/data |
| **Direction** | Peer-to-Peer | Client -> Server |
| **Analogy** | Two colleagues talking to each other | A colleague opening a database |
| **Developed by** | Google (open) | Anthropic (open) |

**Remember:** A2A and MCP are complementary, not competing. An agent can use A2A to talk to other agents AND MCP to access tools.

## Why Does This Matter for Enterprises?

- **Multi-Vendor:** Your orchestrator (Microsoft) can talk to specialists (Google, Salesforce)
- **Microservices for AI:** Each agent is an independent service — scalable, deployable, versionable
- **Security:** HTTP-based = well-known auth patterns (OAuth, API Keys, mTLS)
