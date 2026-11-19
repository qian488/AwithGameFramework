---
name: awith-unity-editor
description: Inspect and operate a Unity project through AwithGameFrame's editor commands. Use for scenes, hierarchy, prefab placement, transforms, and supported native components.
---

# AwithGameFrame Unity Editor

1. Resolve the requested project from `games/<Name>/game.json` or an explicit Unity project path. Check the recorded editor version and read the action list with `tools/unity-editor.ps1 -Action actions`.
2. For a running editor, use the project menu entry `Tools > AwithGameFrame > Enable Editor Automation`, then call the script without `-EditorPath`. For a closed project, pass the matching editor executable. The workspace integration comes from `tools/game.ps1 sync`. Refresh that package through the tool if it predates the command.
3. Run `editor.diagnostics` when the bridge is unavailable, compiling or returning errors. Use the compiler location and the batch log path to locate the failure. For ordinary inspection, use `project.info`, `scene.inspect` or `asset.find`. Include `-Scene` when editing a specific active scene and use unique hierarchy paths.
4. For serialized component fields, use `component.inspect` with the full component type, then `component.set -Preview` with the exact writable paths. Apply the same property map when the preview matches the task. Inspect persisted values and run behavior checks when the task calls for them.
5. Use the closest native action for the edit. Running editor edits use Undo. Persist them with `scene.save` when the task calls for saving. A batch mutation requires both `-Scene` and `-Save`.
6. If Unity claims a request and the command times out, inspect the scene and local result before retrying. For custom Inspector work or unsupported actions, use the relevant game Editor tool or available UI. Create reusable editor code in the proper game or adapter Editor assembly when the work calls for it.
7. Use `scene.transaction` to preview or apply dependent steps in one scene. Check the project allowlist and inspect the result record before retrying a failed transaction. Use `checkpoint.capture` before durable edits that may need to be recovered after a restart. Review the current asset version before passing its hash to `checkpoint.restore`.

Read [the command reference](../../../docs/reference/unity-editor-automation.md) for examples, result semantics and scope. Use [game development](../awith-game-development/SKILL.md) when the decision concerns code ownership or validation rather than an editor action.
