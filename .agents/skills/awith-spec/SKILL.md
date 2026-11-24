---
name: awith-spec
description: Turn an agreed game or framework change into a specification and independently verifiable work slices.
disable-model-invocation: true
---

# Write a Specification

Describe the caller's problem and the desired result using the project's terms. Use the [content design workflow](../awith-content-design/SKILL.md) when an unresolved choice changes the result.

Use the [task brief](../../../agent/templates/task-brief.md) to capture the source decision, affected modules, owner and acceptance. Choose the highest existing public entry point that can verify each outcome. Describe the important failure cases with concrete inputs and expected behavior.

For work with independent deliveries, write one observable result per slice and declare its prerequisites. Use the [AgentKit board](../../../agent/README.md) when the developer wants local issue state, dependencies or handoff. The specification is ready when another session can implement the first slice and demonstrate its acceptance from the artifact.
