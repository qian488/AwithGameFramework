---
name: awith-review
description: Review an AwithGameFrame change against its agreed behavior and repository design conventions. Use before delivery or for a requested diff review.
---

# Review a Change

Choose the diff baseline and locate the request, spec or ticket. Read the relevant AGENTS, module README and ADR.

1. **Spec.** Follow the changed entry points as a caller. Compare the behavior and failure cases with the actual request. Identify missing behavior, added scope and evidence that exercises each affected outcome.
2. **Standards.** Review the same diff for responsibility, dependency direction, lifecycle and data ownership, compatibility, failure diagnostics and whether the public interface hides its own complexity. Use the repository's checks for mechanical rules and inspect the remaining design judgment in the diff.
3. Report the two reviews independently. For each finding give the file, concrete consequence and the rule or request that supports it. When a behavior requires observation in a real engine or Player, record the exact mode and evidence.

Use the [review record](../../../agent/templates/review.md) when a human will accept a persisted AgentKit task.
