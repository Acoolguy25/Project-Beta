# Ryan HUD (`com.ryan.hud`)

In-game HUD prefabs: chat, top bar, player list, vote, spectate, stamina, toolbar, name tags, build menu, credits.

Layer 4: may reference com.ryan.core, com.ryan.ui, com.ryan.networking, com.ryan.gameframework. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.hud": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.hud#<tag>"
```
