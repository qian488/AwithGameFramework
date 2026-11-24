---
name: awith-framework-development
description: Design, implement or diagnose changes to AwithGameFrame framework modules, adapter contracts, and framework tooling. Use for public API, dependency boundaries, regression tests, and architecture refactoring.
---

# AwithGameFrame Framework Development

1. Begin with the [repository guide](../../../GUIDE.md). Resolve the smallest relevant module from its manifest and README. For product or ownership changes, read [product direction](../../../docs/product-direction.md) and the applicable architecture decision.
2. Before changing an interface, locate its callers and state the behavior a check must observe. Resolve terms from the owning module. Ask the maintainer when the repository and conversation leave a choice open that changes ownership, user-visible behavior, or handling of existing data.
3. Place pure rules behind ordinary C# inputs and outputs. Keep lifetime and effects with the owner that can perform them. For a proposed abstraction, identify which callers it simplifies and how its boundary can be tested. Keep game-specific behavior in the game until reuse justifies extraction.
4. Verify a slice through its normal entry point. For regressions, capture the failure and rerun the same check after the change. Use .NET for portable rules and the real engine for native behavior. Update current usage at the owning module. Write migration steps when an actual caller or data set needs them.
5. Run the narrow check, then the relevant repository checks from [verification](../../../docs/reference/verification.md). Report test scope and remaining checks. Use [AgentKit](../../../agent/README.md) when the task needs saved scope, evidence or handoff.

For a reported failure, invoke `awith-diagnose`. For delivery or a requested diff review, invoke `awith-review`. Read the [workflows](references/workflows.md) for design questions, prototypes and module boundaries.
Use the workflow for ticket triage when an agreed spec has multiple slices. Use a bounded prototype for an uncertain choice and a boundary check for a module redesign.
