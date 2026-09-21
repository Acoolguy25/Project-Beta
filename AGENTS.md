# Project-Beta Agent Guide

This file applies to the entire repository. Follow it before making any change.

## Priorities

1. Use Unity CLI and the connected Unity Editor for Unity-owned data.
2. Make the smallest durable change that solves the actual runtime problem.
3. Preserve unrelated user work and existing serialized references.
4. Prefer authored prefabs and existing art under `Assets/` over runtime construction or placeholder geometry.
5. Distinguish compilation, controlled probes, and live gameplay/multiplayer proof in every handoff.

## Project Shape

- Unity version: `6000.5.5f1`.
- The manifest currently pins `com.unity.pipeline` to `0.5.0-exp.1`; re-check this before relying on parameterized `unity command` calls.
- Rendering: URP.
- Networking: FishNet; gameplay state that affects all players must be server-authoritative.
- Shared/reusable code and assets belong under `Assets/RyanAssets/`.
- Universe-specific code, rules, state, presentation, and prefabs belong under `Assets/Universes/UniverseData/<universe>/`.
- Keep client, server, and shared code in the appropriate folders and asmdefs. Do not make a client assembly the authority for server behavior.
- Treat `Assets/FishNet/` and third-party asset folders as vendor code. Avoid modifying them unless the task explicitly requires it.
- Do not edit generated `.csproj`, `.slnx`, `Library/`, `Temp/`, `Logs/`, or package `obj/` output as a source change.

Useful high-level structure:

```text
Assets/
  RyanAssets/                       reusable systems and shared prefabs
    Characters/                     character and NPC foundations
    Client/ClientUI/                reusable runtime UI prefabs and logic
    Commands/                       client/server/shared command framework
    Items/ and Tools/               reusable networked gameplay objects
    Server/                         server core and server features
    Shared/                         shared declarations, components, and globals
    UI/                             reusable UI controls
  Universes/UniverseData/
    <universe>/
      Client/                       universe presentation and client behavior
      Server/                       authoritative runner and server rules
      Shared/                       wire contracts and shared data
      Tests/                        focused universe tests when present
```

## Coding Style

- Follow `.editorconfig`: C# opening braces stay on the same line; `else`, `catch`, and `finally` stay on the closing-brace line.
- Match namespaces to folders. `Assets/RyanAssets` namespaces begin with `RyanAssets`; universe code follows the existing `Universes.UniverseData.<universe>` convention.
- Prefer clear, focused components over large managers and duplicated one-off behavior.
- Reuse an existing setting as the source of truth. Apply requested multipliers or derived behavior instead of introducing a parallel setting.
- Expose intentional tuning through serialized fields or the existing configuration/command system. Validate values at the authority boundary.
- Keep network payload names, namespaces, field order, and field types stable unless a coordinated protocol migration is part of the task.
- Subscribe and unsubscribe event handlers symmetrically. Cancel async work during teardown and never discard task failures silently.
- Avoid per-frame scene searches, repeated allocations, and reflection in hot paths. Cache stable references.
- Do not hide failures with broad null guards. Resolve ownership/lifecycle ordering and log actionable context where recovery is required.

## Prefab-First Authoring

- Author UI, gameplay objects, effects, cameras, audio sources, colliders, and their component wiring in prefabs under `Assets/`.
- Do not construct production UI at runtime with `new GameObject`, `AddComponent`, or scripts that build Canvas/RectTransform/TMP hierarchies. Runtime code may instantiate an authored prefab and bind live data to it.
- Use prefab variants or nested prefabs when several universes share a foundation but need different presentation.
- Store persistent references with serialized fields on the prefab or scene. Wire buttons, labels, icons, audio, and component relationships in the Inspector rather than relying on runtime name searches.
- Preserve prefab connections. Do not unpack a prefab or replace a connected instance with loose scene objects unless explicitly required.
- Before creating visuals, search all of `Assets/` for suitable prefabs, models, materials, sprites, textures, animations, and audio. Prefer the project's authored assets.
- Do not ship blocky placeholder objects, Unity primitives, procedurally generated meshes, or plain colored `MeshRenderer` stand-ins when a suitable asset exists. Debug geometry is acceptable only when editor-only or explicitly requested.
- If no suitable art exists, create a clean prefab hook that can receive the intended asset and report the missing art dependency. Do not silently turn a prototype cube/capsule into production presentation.
- Dynamic/network spawning still starts from an authored prefab. Register and spawn the prefab through the existing FishNet/project path rather than reconstructing it component-by-component.
- Use TextMeshPro and the UI framework already used by the surrounding prefab. Keep layout, anchors, hover detail, navigation, and accessibility in the asset.

## Unity CLI Connection Discipline

Use the absolute project path when targeting an Editor. This project may also use ParrelSync clones, so never assume that the first Editor or process is the intended client/server.

```powershell
$ProjectBetaPath = 'C:\Users\ryanb\Documents\Unity\Project-Beta'
unity status --project-path $ProjectBetaPath --format json
unity command --project-path $ProjectBetaPath --caller plugin --skill unity-cli --detail compact --no-pager
```

- Run `unity status` before any scene, prefab, or asset operation.
- On every `unity command` call, pass `--project-path`, `--caller plugin`, and `--skill <active-skill-name>`.
- When several Editors/clones are open, inspect the returned project paths and target the exact main project or clone required by the task.
- A domain reload can briefly drop the connection. Re-run `unity status`, then continue only after the target reports `ready`.
- If the CLI cannot see an Editor that the user says is open, check `unity pipeline list` and compile/Safe Mode state. A sandbox can also hide a live Editor; do not treat one failed status check as proof it is closed.
- Discover the current command schema instead of guessing arguments. The connected Editor's catalog is authoritative.

Current package compatibility:

- Pipeline `0.5.0-exp.1` can advertise the command catalog but may reject forwarded command parameters with `too old to parse command lines`, and some status calls may time out.
- Confirm with `unity pipeline list`. Do not repeatedly retry the same failing invocation.
- The durable fix is `unity pipeline upgrade --project-path $ProjectBetaPath`, but a package upgrade is a project dependency change: do it only when it is within the requested scope, then let Unity resolve/reload and re-check `unity status`.
- If the pinned version must remain, use the connected Editor's available authenticated local API/MCP equivalent for execution. Never print, persist, or commit its token. Continue to target the exact project/clone and use Unity Editor APIs for asset changes.

```powershell
unity command --project-path $ProjectBetaPath --caller plugin --skill unity-cli --query prefab --detail full --format json --no-pager
unity command --project-path $ProjectBetaPath --caller plugin --skill unity-cli --query scene --detail compact --no-pager
unity command --project-path $ProjectBetaPath --caller plugin --skill unity-cli --query test --detail compact --no-pager
```

## Useful Connected-Editor Commands

The parameterized examples below are the preferred interface once Pipeline is `0.6.0-exp.1` or newer. On the currently pinned `0.5.0-exp.1`, use them to identify intent and command names, then use the compatible connected-Editor API described above rather than falling back to raw Unity YAML edits.

Inspect before editing:

```powershell
unity command get_scene_hierarchy --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
unity command list_open_scenes --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
unity command find_assets --project-path $ProjectBetaPath --caller plugin --skill unity-cli --type GameObject --name Topbar --search_in Assets --format json
unity command get_component_properties --project-path $ProjectBetaPath --caller plugin --skill unity-cli --target '<object-ref>' --format json
unity command get_serialized_fields --project-path $ProjectBetaPath --caller plugin --skill unity-cli --target '<object-ref>' --format json
```

Prefab and scene authoring:

```powershell
unity command instantiate_prefab --project-path $ProjectBetaPath --caller plugin --skill unity-cli --prefab 'Assets/Path/Thing.prefab' --format json
unity command create_prefab --project-path $ProjectBetaPath --caller plugin --skill unity-cli --source '<object-ref>' --path 'Assets/Path/Thing.prefab' --format json
unity command apply_prefab_overrides --project-path $ProjectBetaPath --caller plugin --skill unity-cli --instance '<object-ref>' --format json
unity command save_scene --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
unity command save_all --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
```

- Prefer built-in declarative commands for straightforward edits.
- For complex prefab changes, use `eval_file` with a small temporary Editor C# script using `PrefabUtility`/`SerializedObject`; read the prefab back afterward. Do not hand-edit `.prefab`, `.unity`, or `.asset` YAML while the Editor is reachable.
- Use `dry_run` when a listed command supports it. Save only after checking the exact target and effect.
- After a save, inspect `git diff` because Unity can serialize incidental changes or remove overrides.

Complex Editor operation:

```powershell
unity command eval_file --project-path $ProjectBetaPath --caller plugin --skill unity-cli --file 'C:\path\to\focused_probe.cs' --timeout 30000 --format json
```

Compile and console verification:

```powershell
unity command recompile --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
unity command recompile_status --project-path $ProjectBetaPath --caller plugin --skill unity-cli --format json
unity command get_console_logs --project-path $ProjectBetaPath --caller plugin --skill unity-cli --severity error --limit 200 --format json
```

- Poll `recompile_status` until `completed` or `up_to_date`; triggering compilation is not completion.
- Clear old logs only when needed to isolate a test, then inspect new errors and exceptions.
- Use Unity compilation for Unity-generated assemblies. A plain `dotnet build` is supplementary and may fail because Unity-generated restore assets are absent.
- For server-only code, compile/probe with the appropriate `UNITY_SERVER` and `SERVER_BUILD` context, then restore the original role/defines. Use a real server prefab/profile when lifecycle dependencies matter.

Runtime and test commands available in this project include:

```text
editor_play, console, get_console_logs,
list_tests, run_tests, test_status, cancel_tests,
recompile, recompile_status, eval, eval_file,
capture_scene_view
```

Query their live schema before use. Do not enter Play Mode, switch build targets, or run a destructive/long-running operation without confirming it is appropriate for the active user session.

## Change Workflow

1. Read `git status --short` and preserve all unrelated edits.
2. Trace the real active path: scene, runner, prefab, asmdef, client/server boundary, and runtime instance.
3. Search for existing implementation and assets before adding files or concepts.
4. Make the narrowest long-term fix in the correct shared or universe-specific location.
5. For asset changes, use the connected Editor and read back serialized values/references.
6. Recompile, poll to completion, and inspect fresh console errors.
7. Run the smallest focused edit/play test or controlled probe that exercises the changed behavior.
8. Review `git diff --check`, the focused diff, and `git status --short`. Do not clean or revert unrelated work.
9. Report what was actually verified and name any remaining live gameplay, dedicated-server, or multiplayer gap.

## Verification Standards

- Source inspection proves only source structure.
- Successful recompilation plus a clean console proves compilation, not gameplay.
- A controlled Editor probe proves only the conditions exercised by that probe.
- Client-only Play Mode cannot prove dedicated-server behavior.
- A server-prefab probe is stronger than an isolated component probe but is not a live multiplayer round.
- Physics, navigation, input, audio, UI, network spawning, and timing changes should receive a focused behavioral probe whenever possible.
- For "still broken" or "check it right now," reproduce through the active scene/runner/runtime path rather than stopping after a compile.

## Safety and Source Control

- Never overwrite or revert user changes just to obtain a clean tree.
- Avoid raw YAML edits, GUID fabrication, and manual `.meta` creation when Unity can create/import the asset safely.
- Do not delete assets without checking references and confirming the exact target.
- Do not switch build target, alter global defines, or save open scenes as a side effect unless required; restore temporary settings after the probe.
- Keep secrets, auth tokens, machine-local paths, and generated caches out of source control.
