---
name: awith-diagnose
description: Reproduce and diagnose a reported build, gameplay, Unity editor, or performance failure using the affected entry point.
---

# Diagnose a Failure

1. Record the expected and actual result. Run the shortest command or scenario that reaches the reported failure and assert the symptom. For Unity compilation start with `tools/unity-editor.ps1 -Action editor.diagnostics`. For a gameplay rule use its public command or test. For performance capture the same workload and metric on both runs.
2. Record the invocation, relevant output and actual version. Minimize the fixture. Pin the input, seed and time source where possible. For intermittent failures, record the attempts and failures.
3. Test plausible causes with observations that distinguish them. Use a compiler location, targeted log, breakpoint or data snapshot at the first failing boundary. Treat a result from a different mode or platform as evidence for that mode.
4. Fix the verified cause. Rerun the same check, then the relevant broader regression. Remove temporary probes. The diagnosis is ready to hand off when another session can reproduce the observed failure or rerun the regression from the recorded inputs.

See [verification commands](../../../docs/reference/verification.md) for isolated background runs.
