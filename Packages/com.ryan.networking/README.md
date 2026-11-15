# Ryan Networking (`com.ryan.networking`)

Backend authentication and login, client connection core and modules, server modules, and player levels.

Layer 2: may reference com.ryan.core, com.ryan.ui. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.networking": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.networking#<tag>"
```
