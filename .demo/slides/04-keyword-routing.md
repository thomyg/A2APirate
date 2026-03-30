---
theme: default
layout: intro
transition: fadeIn
---

# Routing Without AI

The AgentCard **is** the routing table.

---
layout: default
transition: slideRight
---

# How Keyword Routing Works

Each agent's AgentCard contains **skills** and **tags**:

```json
{
  "skills": [{
    "name": "Pirate Speech",
    "tags": ["pirate", "fun", "persona"]
  }]
}
```

```json
{
  "skills": [{
    "name": "Dictionary Lookup",
    "tags": ["no-ai", "dictionary", "a2a", "protocol", "educational"]
  }]
}
```

User types "what is a2a?" → match on tag `"a2a"` → **DictionaryAgent wins**.

---
layout: default
transition: slideRight
---

# Scoring System

| Match Type | Points | Example |
|---|---|---|
| Agent name in input | +10 | "ask the pirate" → PirateSpecialist |
| Skill name match | +5 | "dictionary lookup" → DictionaryAgent |
| Tag match | +3 | "protocol" → DictionaryAgent |
| Description word | +1 | "question" → PirateSpecialist |

Highest score wins. Tie? First agent gets it.

---
layout: section
transition: fadeIn
---

# No API key. No latency. No tokens spent.

Just metadata matching. AI is optional.
