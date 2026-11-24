# Framework and Game Development Workflows

Use the selected module or game README for the terms and boundaries in these workflows. Use the [AgentKit task brief](../../../../agent/templates/task-brief.md) when the task benefits from a written scope or handoff.

## Clarify a Design

1. Classify the request before implementation. Separate framework capability, game-specific implementation, content design and presentation acceptance. A request to add a map, Boss, weapon, UI, effect or story is a content request when the intended content has not been specified.
2. Identify the decisions that change the user-visible result. Ask about the player goal, content roster, gameplay role, visual or audio direction, difficulty, scope, references, platform constraints and acceptance standard. Bundle related questions so the developer can answer them together.
3. Do not create dependent game content while those decisions are unresolved. You may inspect the repository, prepare a proposal, build independent tooling and identify affected modules. Wait for the developer's decision before writing the map layout, enemy roster, Boss behavior, art direction, UI arrangement or similar content.
4. If the developer explicitly delegates design, present a concrete design proposal with alternatives, assumptions and acceptance examples. Treat implementation as authorized after the proposal is accepted, or when the request explicitly says to decide and implement without an approval step.
5. Explain the intended behavior with one concrete caller example and one boundary case. Check code, module READMEs and [existing decisions](../../../../docs/architecture/overview.md) for answers.
6. Record agreed domain terms in the owning module or game README. Record a durable framework-wide tradeoff in the architecture decisions. Put the decision source, unresolved assumptions, observable acceptance criteria and agreed scope in the task brief when one is in use.

For a new editor command, describe its input and result in terms of the engine API. Record game-specific types, asset paths and acceptance examples with the game that owns them.

## Write a Specification

1. Use the [task brief](../../../../agent/templates/task-brief.md) when an agreed behavior needs a written spec or handoff. Write the caller's problem, affected modules and contracts, chosen design, and one observable result per acceptance item. Include failure behavior when it matters to the caller.
2. If the task uses AgentKit, create or update its task manifest and reference it in the brief. Match the numbered acceptance items to `acceptance`. Select the owning modules in `readModules`, bound modifications with `writePaths`, and use `requiredChecks` for checks that can establish automated evidence. Put any manual checks and the intended reviewer in the brief.
3. Implement one slice at a time. Record the command, input and outcome beside the acceptance item, or the actual observation for a manual check. Recollect context after edits and use `check` and `report` for the current state. Hand off the brief, task manifest, report and the evidence needed to rerun a check. Use the [AgentKit workflow](../../../../agent/README.md#一次任务的闭环).

## Slice and Triage Work

1. Take the agreed brief and write each slice around a result that a consumer can verify without waiting for the entire feature. For each slice, record acceptance criteria, the smallest write scope and a catalog check. Put prerequisites in `dependsOn` and record the decision source.
2. Import the local board, assign an owner and priority, and add the triage reason. Link an external issue when one exists. Compare an exported tracker snapshot and bring discrepancies to the project's review.
3. Start a ready ticket and hand its task path to the next session. From that path, collect context, execute checks and record the handoff. Keep machine results and human acceptance separate. See [AgentKit tickets](../../../../agent/README.md#工单分诊与接手).

## Prototype an Uncertain Choice

1. Name the unanswered question and the decision the observation will inform. Choose the smallest observable behavior and specify a pass or discard condition before writing the spike.
2. Put disposable inputs and outputs in an isolated fixture or the owning game workspace. Run both alternatives through the same consumer boundary. Record the version, fixtures, commands and observed difference. For Unity behavior, use the real editor.
3. Recommend adoption, another experiment or stopping. If adopted, translate the finding into a regression check and a bounded delivery ticket. Keep throwaway fixtures separate from shipped source and assets.

## Improve a Module Boundary

1. Start from a concrete maintenance problem. Trace two callers or two changes through their modules and identify the dependency, duplicated decision or lifetime that forces them to change together.
2. Write down the current invariant and check it through the closest public entry point. Describe an alternative that reduces the changed surfaces. Confirm dependency direction in the module manifests and locate engine-owned effects at the adapter boundary.
3. Decide whether the problem compounds future work. Consider current callers, likely follow-up changes, repeated diagnostic or validation cost, and the scope of an immediate fix. Fix a recurring problem in the current slice. Record a one-off or wide-scope problem with a concrete trigger before deferring it.
4. Make the smallest proposed change as a ticket, then rerun the behavior and dependency checks. Compare changed modules and results in the handoff. Record a durable change of ownership or contract in the architecture decisions and let the project reviewer decide adoption.

## Diagnose a Failure

1. Capture expected and actual behavior, engine and mode, inputs, and the first failing boundary. Reproduce with the shortest real command, test or scene interaction that fails for the reported reason. If intermittent, record the reproduction rate and inputs.
2. Compare plausible causes using an observation at the boundary that distinguishes them. Retain the smallest observable regression check. For an editor command problem, distinguish project discovery, package import, action execution and persisted editor state.
3. Run the same check before and after the fix and record the results. When reproduction is unavailable, record what was observed and which diagnostic check would distinguish the remaining causes.

## Deliver and Review a Slice

1. Describe the shortest user-visible path through the affected boundaries. For a core rule or a repeatable regression, run one check through the caller's entry point and capture its failure before changing the behavior. Make the smallest change that passes the same check. Review structure after the behavior passes. For a reusable editor command, use an independent engine project. For a gameplay task, use the rule, the game asset that consumes it, and the observable behavior in the selected game.
2. Review the diff against the agreed behavior, then review ownership, compatibility, failure paths and code understandability against repository conventions. Confirm that each new abstraction has a real caller, that abnormal exits have diagnostic context, and that deferred debt has a concrete trigger.
3. Link the evidence to each outcome and use the [review checklist](../../../../agent/templates/review.md) when recording a review. Carry gaps into the [handoff](../../../../agent/templates/handoff.md) where applicable.

The interview sequence, public-entry-point checks and separate spec and standards reviews draw on [grilling](https://github.com/mattpocock/skills/tree/abeeaf95660ba9e8b7c753eb38f441ca707a8823/skills/productivity/grilling), [tdd](https://github.com/mattpocock/skills/tree/abeeaf95660ba9e8b7c753eb38f441ca707a8823/skills/engineering/tdd) and [code-review](https://github.com/mattpocock/skills/tree/abeeaf95660ba9e8b7c753eb38f441ca707a8823/skills/engineering/code-review).
