---
name: awith-framework-development
description: Design, implement or diagnose changes to AwithGameFrame framework modules, adapter contracts, and framework tooling. Use for public API, dependency boundaries, regression tests, and architecture refactoring.
---

# AwithGameFrame Framework Development

1. Begin with the [repository guide](../../../GUIDE.md). Resolve the smallest relevant module from its manifest and README. For product or ownership changes, read [product direction](../../../docs/product-direction.md) and the applicable architecture decision.
2. State the affected consumer, observable behavior and acceptance check before changing the interface. Resolve vocabulary from existing APIs and documentation. Ask about an unresolved choice only when it changes compatibility, ownership or the promised outcome.
3. Place pure rules behind ordinary C# inputs and outputs. Keep lifetime and effects with the owner that can perform them. For a proposed abstraction, identify which callers it simplifies and how its boundary can be tested. Keep game-specific behavior in the game until reuse justifies extraction.
4. Work in a vertical slice where practical. Reproduce regressions through a public boundary, make the smallest change that satisfies the behavior, then extend coverage. Use pure .NET for portable rules and the real engine for native behavior. Keep API migration notes or an architecture decision when the change affects consumers or durable constraints.
5. Run the narrow check, then the relevant repository checks from [verification](../../../docs/reference/verification.md). Report test scope and remaining checks. Use [AgentKit](../../../agent/README.md) when the task needs saved scope, evidence or handoff.

Choose a [workflow](references/workflows.md) when the next step depends on the task. Use design clarification for unresolved contracts, a specification for agreed work that needs acceptance criteria or handoff, and diagnosis for a failure. Use delivery and review to connect the implementation to its evidence.
Use the workflow for ticket triage when an agreed spec has multiple slices. Use a bounded prototype for an uncertain choice and a boundary check for a module redesign.
