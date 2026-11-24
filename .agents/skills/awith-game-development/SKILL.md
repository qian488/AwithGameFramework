---
name: awith-game-development
description: Build or diagnose a concrete game using AwithGameFrame. Use for game workspaces, external Domain and Application code, engine integration, and validation of playable features.
---

# Develop a Game with AwithGameFrame

For an unresolved player-facing decision, invoke `awith-content-design`. For a reported failure, invoke `awith-diagnose`. If a framework entry point is unfamiliar, use `tools/agent.ps1 capabilities --query <term>`.

1. Start from the selected game's `game.json`, README and AGENTS. Use its module manifest to find framework services and identify which engine and scenarios the game actually supports.
2. Describe one observable outcome in the game's vocabulary. Check ownership up front. Domain holds deterministic rules, Application orchestrates them, the client owns native assets and device or scene integration, and the server owns process infrastructure. Share contracts and pure rules through project references.
3. Build a small slice from rule to application to the real client. Use the selected game's README for concrete examples and asset paths. Test calculations with .NET and check native authoring and playback through the selected editor. Keep art-side parameters in engine assets and pass values to portable calculations.
4. Use `tools/game.ps1 build` and `sync` for external code updates. Preserve generated directory ownership and engine asset IDs. For Unity scene operations use [Unity Editor](../awith-unity-editor/SKILL.md). For the independent server use `run-server` when the slice needs it.
5. Record the exact game behavior, engine and test mode exercised. Include separate evidence for editor behavior, Player builds and Player execution when those are part of the requested outcome.

See [game-workspace commands](../../../docs/getting-started/game-workspace.md).

Use the shared [development workflows](../awith-framework-development/references/workflows.md) for design clarification, specifications, reproducible diagnosis, ticket handoffs and prototypes. Select the generated game module in `readModules` and write each acceptance item in the game's language. For a change to Unity Inspector values, use the editor skill to inspect, preview and verify the exact serialized values.
