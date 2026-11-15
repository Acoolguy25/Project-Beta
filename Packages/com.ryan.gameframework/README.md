# Ryan Game Framework (`com.ryan.gameframework`)

Server runner and features, characters, tools, items, combat, cameras, platformer mechanics, network manager prefabs and authoring tools that every universe builds on.

Layer 3: may reference com.ryan.core, com.ryan.ui, com.ryan.networking. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.gameframework": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.gameframework#<tag>"
```
