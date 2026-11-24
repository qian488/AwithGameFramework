---
name: awith-content-design
description: Clarify unresolved game content requests before implementation. Use when a request adds or changes maps, enemies, bosses, weapons, skills, UI, effects, audio, story or other player-facing content.
---

# Clarify Game Content Before Implementation

1. Read the game's README and the relevant design or existing content. Resolve anything the game or conversation already answers.
2. Ask the independent decisions the developer can answer now, with a recommended answer and a concrete consequence for each. Once the developer replies, identify which dependent decisions are ready. For a new boss, settle its role and the intended fight before asking about phase timings. Continue independent technical work while awaiting an answer.
3. When design is delegated, state the assumptions and the intended player-visible outcome. For an exploratory request, present a proposal. For a request to design and implement, proceed within that scope.
4. Restate the agreed result as an observable player path and one relevant boundary case. Record stable domain terms in the owning game documentation when the terms enter the player-facing spec and code. Record durable cross-module choices in the existing architecture decisions. A task brief can preserve the agreed behavior when another session will implement it.

The design is ready when a developer can identify which behavior is requested and how to observe it. Use the [task brief](../../../agent/templates/task-brief.md) when recording a task in AgentKit.
