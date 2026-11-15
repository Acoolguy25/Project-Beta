# Ryan Commands (`com.ryan.commands`)

Chat command framework: server command handlers and the client command console.

Layer 5: may reference com.ryan.core, com.ryan.ui, com.ryan.networking, com.ryan.gameframework, com.ryan.hud. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.commands": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.commands#<tag>"
```
