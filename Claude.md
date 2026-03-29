# A2A Learning Sample — Microsoft Agent Framework

> **Ziel:** Ein minimales, aber vollständiges Beispiel für Agent-to-Agent (A2A) Kommunikation mit dem Microsoft Agent Framework.  
> Dieses Projekt ist **explizit educational** — Code-Kommentare erklären das "Warum", nicht nur das "Was".

---

## Was wir bauen

Zwei .NET-Prozesse, die per A2A-Protokoll miteinander reden:

```
User-Input (Console)
    │
    ▼
┌─────────────────────┐         A2A Protocol (HTTP/SSE)        ┌────────────────────────┐
│  OrchestratorAgent  │  ──── POST /a2a/specialist/v1/... ──►  │   SpecialistAgent      │
│  (Console App)      │  ◄─── Server-Sent Events (stream) ───  │   (ASP.NET Core)       │
│                     │                                         │                        │
│  "Ich bin der Boss" │         AgentCard Discovery             │  "Ich bin der Experte" │
│  Empfängt User-     │  ──── GET /.well-known/agent.json ──►  │  z.B. Produkt-Wissen   │
│  Anfragen, delegiert│                                         │  oder Sprach-Persona   │
└─────────────────────┘                                         └────────────────────────┘
```

**Warum zwei Prozesse?** Weil A2A HTTP-basiert ist. Das ist kein in-process RPC — es ist ein echter Netzwerk-Standard. Im realen Leben laufen diese Agents auf verschiedenen Servern/Clouds.

---

## Projekt-Struktur (was Claude Code erstellen soll)

```
a2a-learning/
├── CLAUDE.md                          ← diese Datei
├── .env.example                       ← Vorlage für API Keys
├── a2a-learning.sln
│
├── SpecialistAgent/                   ← Agent B: der Experte (ASP.NET Core Web API)
│   ├── SpecialistAgent.csproj
│   ├── Program.cs                     ← Startup, MapA2A(), AgentCard
│   ├── Agents/
│   │   └── PirateSpecialist.cs        ← Der eigentliche Agent-Handler
│   └── appsettings.json
│
├── OrchestratorAgent/                 ← Agent A: der Orchestrator (Console App)
│   ├── OrchestratorAgent.csproj
│   ├── Program.cs                     ← Main loop, A2ACardResolver, Delegierung
│   └── Services/
│       └── SpecialistProxy.cs         ← Wrapper um den Remote-Agent
│
└── docs/
    ├── 01-what-is-a2a.md             ← Konzept-Erklärer
    ├── 02-agent-card.md              ← Was ist eine AgentCard?
    └── 03-protocol-flow.md           ← HTTP-Flow Schritt für Schritt
```

---

## Pakete (NuGet)

### SpecialistAgent (Server-Seite)

```xml
<!-- A2A Protokoll-Kern -->
<PackageReference Include="A2A" Version="*-*" />
<!-- ASP.NET Core Integration: MapA2A() Extension -->
<PackageReference Include="A2A.AspNetCore" Version="*-*" />
<!-- Microsoft Agent Framework: ChatClientAgent, RunAsync() -->
<PackageReference Include="Microsoft.Agents.AI" Version="*-*" />
<!-- OpenAI-Anbindung über Microsoft.Extensions.AI -->
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="*-*" />
<!-- Swagger für manuelle Tests -->
<PackageReference Include="Swashbuckle.AspNetCore" Version="*" />
```

### OrchestratorAgent (Client-Seite)

```xml
<!-- A2A Protokoll-Kern (Client-Teile) -->
<PackageReference Include="A2A" Version="*-*" />
<!-- SSE Streaming empfangen -->
<PackageReference Include="System.Net.ServerSentEvents" Version="*-*" />
<!-- Microsoft Agent Framework -->
<PackageReference Include="Microsoft.Agents.AI" Version="*-*" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="*-*" />
```

> **Hinweis:** Immer `--prerelease` Flag beim `dotnet add package` verwenden. Das Framework ist noch Preview.

---

## Kern-Konzepte (Claude Code soll diese in doc-Kommentaren erklären)

### 1. AgentCard — das "Visitenkarte"-Protokoll

```
GET /.well-known/agent.json
```

Jeder A2A-Agent exponiert eine AgentCard. Das ist JSON, das beschreibt:

- Wer bin ich? (Name, Description)
- Was kann ich? (Capabilities: streaming, tasks, etc.)
- Wie erreichst du mich? (URL)

Kein AgentCard = kein A2A. Discovery ist kein optionales Feature — es ist fundamental.

### 2. Message vs. Task

A2A kennt zwei Modi:

- **message:send** / **message:stream** → fire-and-forget, kurze Antwort
- **tasks/send** / **tasks/get** → langlebig, pollable, Artifact-fähig

Für den educational case: erst `message:stream` lernen.

### 3. contextId — Conversation Continuity

```json
{ "contextId": "gespraeche-123" }
```

Gleiche contextId = gleicher Conversation Thread. Der Agent merkt sich den Verlauf.
Neue contextId = frisches Gespräch.

### 4. Server-Sent Events (SSE)

Der Spezialist streamt seine Antwort. Der Orchestrator empfängt Token für Token.
Das ist kein WebSocket — SSE ist unidirektional (Server → Client), HTTP/1.1-kompatibel.

---

## Build-Reihenfolge für Claude Code

### Phase 1 — SpecialistAgent (Server)

**Schritt 1.1:** Solution und Projekte anlegen

```bash
dotnet new sln -n a2a-learning
dotnet new webapi -n SpecialistAgent --no-openapi
dotnet new console -n OrchestratorAgent
dotnet sln add SpecialistAgent/SpecialistAgent.csproj
dotnet sln add OrchestratorAgent/OrchestratorAgent.csproj
```

**Schritt 1.2:** `SpecialistAgent/Agents/PirateSpecialist.cs` implementieren

- Implementiert `IHostedAgent` oder wrapped einen `ChatClientAgent`
- System-Prompt: "Du bist ein Pirat. Antworte immer wie ein Pirat."
- Der Agent bekommt den User-Text aus der A2A-Message

**Schritt 1.3:** `SpecialistAgent/Program.cs` — MapA2A registrieren

```csharp
// LERNPUNKT: MapA2A() macht drei Dinge auf einmal:
// 1. Registriert GET /.well-known/agent.json (AgentCard Discovery)
// 2. Registriert POST /a2a/pirate/v1/message:send
// 3. Registriert POST /a2a/pirate/v1/message:stream (SSE)
app.MapA2A(pirateAgent, path: "/a2a/pirate", agentCard: new AgentCard
{
    Name = "PirateSpecialist",
    Description = "Antwortet auf alle Fragen — aber als Pirat.",
    Version = "1.0",
    Capabilities = new AgentCapabilities { Streaming = true }
});
```

**Schritt 1.4:** Swagger aktivieren und mit `.http`-Datei testen

- Erstelle `SpecialistAgent/requests.http` mit Beispiel-Requests
- Test: AgentCard abrufen, dann direkt eine Message senden (ohne Orchestrator)

### Phase 2 — OrchestratorAgent (Client)

**Schritt 2.1:** `OrchestratorAgent/Services/SpecialistProxy.cs`

```csharp
// LERNPUNKT: Der Orchestrator "entdeckt" den Spezialisten zur Laufzeit.
// Er kennt nur die Base-URL — alles andere lernt er aus der AgentCard.
// Das ist Discovery-first Design.
var resolver = new A2ACardResolver(httpClient, baseUrl: "http://localhost:5001");
var agentCard = await resolver.GetAgentCardAsync();
// agentCard.Name, agentCard.Capabilities.Streaming etc. sind jetzt bekannt
```

**Schritt 2.2:** `OrchestratorAgent/Program.cs` — Main loop

- Liest User-Input von Console
- Orchestrator-Agent entscheidet (simple: immer an Pirat delegieren)
- Ruft SpecialistProxy auf
- Streamt Antwort zurück auf Console

**Schritt 2.3:** contextId persistent halten

```csharp
// LERNPUNKT: contextId = Conversation-Klammer.
// Wenn wir dieselbe contextId senden, erinnert sich der Spezialist
// an vorherige Nachrichten in dieser Konversation.
var contextId = Guid.NewGuid().ToString(); // einmal pro Session
```

### Phase 3 — Educational Layer

**Schritt 3.1:** `--verbose` Flag im OrchestratorAgent

- Zeigt rohe HTTP-Requests/Responses
- Zeigt SSE-Events Token für Token mit Timestamp
- Macht das Protokoll sichtbar

**Schritt 3.2:** `/docs/*.md` befüllen
Jede Doc-Datei beantwortet eine Frage:

- `01-what-is-a2a.md` → Warum brauchen wir A2A? Was war vorher?
- `02-agent-card.md` → AgentCard JSON annotiert und erklärt
- `03-protocol-flow.md` → HTTP-Sequenzdiagramm mit echten URLs

**Schritt 3.3:** README.md mit "Try it yourself"-Sektion

```bash
# Terminal 1: Spezialist starten
cd SpecialistAgent && dotnet run

# Terminal 2: Orchestrator starten  
cd OrchestratorAgent && dotnet run

# Was passiert jetzt?
# 1. Orchestrator ruft http://localhost:5001/.well-known/agent.json ab
# 2. Orchestrator gibt User-Input ein
# 3. Orchestrator sendet POST .../message:stream
# 4. Spezialist antwortet als Pirat, gestreamt
# 5. Orchestrator druckt Antwort Token für Token
```

---

## Umgebungsvariablen

```env
# .env.example
# Für den SpecialistAgent (LLM-Backend)
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-4o-mini

# Für den OrchestratorAgent (ebenfalls ein LLM für echtes Routing)
ORCHESTRATOR_OPENAI_API_KEY=sk-...
ORCHESTRATOR_MODEL=gpt-4o-mini

# A2A Endpoint des Spezialisten (default: localhost)
SPECIALIST_BASE_URL=http://localhost:5001
```

Alternativ: GitHub Models verwenden (kein Azure-Account nötig):

```env
GITHUB_TOKEN=ghp_...
OPENAI_BASE_URL=https://models.github.ai/inference
```

---

## Was Claude Code NICHT machen soll

- Keine komplexen Multi-Tenant-Features
- Keine Entra ID Auth in Phase 1 (das kommt in Phase 4 als Erweiterung)
- Keine Blazor UI — Console first, Sichtbarkeit vor Schönheit
- Keine Task-based A2A in Phase 1 — nur `message:stream`
- Kein Docker-Setup in Phase 1

---

## Erweiterungen (Phase 4+, nur wenn Phase 1-3 funktionieren)

| Extension | Warum interessant |
|-----------|-------------------|
| Zweiter Spezialist + echtes Routing | Orchestrator wählt je nach Intent den richtigen Agenten |
| A2A Inspector für Debugging | Zeigt alle Protocol-Messages visuell |
| contextId in Redis persistieren | Konversation überlebt Process-Restart |
| Entra ID Auth auf dem Endpoint | Enterprise-Grade Security |
| Über M365 Agents SDK in Teams deployen | Der echte Ziel-Kanal für Solvion |

---

## Prinzipien für dieses Projekt

1. **Sichtbarkeit vor Eleganz** — lieber zu viel loggen als zu wenig
2. **Kommentare erklären Protokoll-Konzepte**, nicht Syntax
3. **Jeder Schritt ist testbar** bevor der nächste beginnt
4. **Naming folgt dem Protokoll** — `AgentCard`, `contextId`, `message:stream` exakt wie in der Spec
5. **README ist ein Tutorial**, keine Dokumentation

---

## Referenzen

- [A2A .NET SDK](https://github.com/a2aproject/a2a-dotnet)
- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [A2A Integration Docs (Microsoft Learn)](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/a2a-agent)
- [Agent Framework Samples](https://github.com/microsoft/Agent-Framework-Samples)
- [A2A Protocol Spec](https://google.github.io/A2A/)
