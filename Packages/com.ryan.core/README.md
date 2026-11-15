# Ryan Core (`com.ryan.core`)

Foundation: core helpers, input, tweening, data service, network settings, shared declarations/requests/globals/components, world UI, command definitions, client effects, shaders.

Layer 0: may reference nothing else in this repo. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.core": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.core#<tag>"
```
