# Packaging tools

Project-Beta's shared code lives in embedded UPM packages under `Packages/com.ryan.*` (one
monorepo). Universes stay in `Assets/Universes/` and depend on the packages, never the reverse.

| Layer | Package | Holds |
|---|---|---|
| 0 | `com.ryan.core` | Core, Input, TweenService, DataService, NetworkService, shared declarations/requests/globals/components, WorldUI, command definitions, the universe registry |
| 1 | `com.ryan.ui` | UI controls, prompts, game settings, client audio, command bar UI |
| 2 | `com.ryan.networking` | Authentication, login, client connection core/modules, server modules, player levels |
| 3 | `com.ryan.gameframework` | Server runner/features, characters, tools, items, combat, cameras, network manager prefabs, authoring tools |
| 4 | `com.ryan.hud` | Chat, top bar, player list, vote, spectate, stamina, toolbar, name tags, build menu |
| 5 | `com.ryan.commands` | Chat command console and server command handlers |

The layers follow the asmdef references that already existed (for example, characters use the
settings UI, so UI controls sit below the game framework). `packages.json` is the source of truth.

- `asmdef_graph.py` prints the asmdef graph; `--check` fails on an upward reference, a package
  referencing game code, a universe referencing another universe, or a cycle; `--dot` emits Graphviz.
- `move_to_packages.py` did the move: `git mv` of each path with its `.meta` (GUIDs, and so every
  prefab and scene reference, are unchanged), package.json/README/CHANGELOG per package, manifest
  entries, and rewriting of hard-coded `Assets/RyanAssets/...` strings. It is idempotent: add a new
  `Assets/RyanAssets/<Folder>` to a package's `paths` in `packages.json` and run it again.

FishNet's prefab generator is set to search `Assets*` and `Packages*`
(`Assets/FishNet.Config.XML`), so networked prefabs inside packages stay in DefaultPrefabObjects.

To use a package from another Unity project, reference it by git URL with a path and a tag:
`"com.ryan.core": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.core#<tag>"`.
