# CLAUDE.md
<!-- Funplay Unity MCP managed project skills -->

# Funplay Unity MCP Project Guidance

This file is managed by Funplay MCP for Unity for Claude Code.

## Installed skills

- `unity-mcp-workflow` - Efficient workflow for using Unity MCP to edit, import, compile, inspect, and test Unity projects.

## Preferred workflow

- Use Funplay MCP tools for Unity editor state and automation.
- Use `execute_code` for non-trivial Unity orchestration. For new snippets, implement `IFunplayCommand` and use `ctx.RegisterObjectCreation` / `RegisterObjectModification` / `DestroyObject` so changes participate in Undo and `ctx.Log` for traceable output.
- Inspect Unity objects through MCP before changing user-named scene or prefab targets. Carry the returned `instanceId` into follow-up calls (`find_method=by_id`) instead of re-resolving by name.
- Tool returns are structured JSON (`{success, message, data}` / `{success: false, code, error, data}`). Branch on `code`, not free-form text.
- Set component fields with `set_component_property(ies)` — it picks up `[SerializeField] private` fields and accepts Object references as `{"fileID": <instanceId>}` or `{"assetPath": "Assets/..."}`.
- Read editor state through `get_selection`, `get_prefab_stage`, `get_tags`, `get_layers`, `get_build_settings`; try `execute_menu_item` before writing ad-hoc `execute_code`.
- Save only the scene or prefab assets intentionally modified, then read back exact values.
- With default `core` exposure, use the focused workflow tools. With default `full` exposure, prefer specific MCP tools for simple editor operations.
- `execute_code` refreshes assets and waits for compilation before running. For other tools that depend on freshly compiled code, still call `request_recompile` after external script edits.
- `request_recompile` is rejected while Unity is in Play Mode. Call `exit_play_mode` first, then retry.
- After `enter_play_mode`, the HTTP server briefly drops while Unity reloads the domain. Poll `tools/list` or `get_reload_recovery_status` until it responds again before issuing the next tool call.
- If domain reload interrupts a request, follow with `get_reload_recovery_status`.
- Additional installed skills are available under `.claude/skills/`.

## Project

- Project root: `D:\unity project\Convai-English-learning-game`
- Product name: `convaiquick`
