# Ryan UI (`com.ryan.ui`)

UI controls (Textbox, ListGrid, ButtonGrid, Autocomplete), prompts, game settings, client audio and the command bar UI.

Layer 1: may reference com.ryan.core. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.ui": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.ui#<tag>"
```
