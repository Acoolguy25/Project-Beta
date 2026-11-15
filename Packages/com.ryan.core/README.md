# Ryan Core (`com.ryan.core`)

Foundation: core helpers, input, tweening, data service, network settings, shared declarations/requests/globals/components, world UI, command definitions, client effects, shaders.

Layer 0: may reference nothing else in this repo. Never a universe, and never a package in a higher layer (checked by `Tools/Packaging/asmdef_graph.py --check`).

Files under `Runtime/` keep the path they had under `Assets/RyanAssets/`. Embedded in Project-Beta; another Unity project can use it through a git URL:

```json
"com.ryan.core": "https://github.com/Acoolguy25/Project-Beta.git?path=/Packages/com.ryan.core#<tag>"
```

## Player progress (`Runtime/Progress`, `RyanAssets.Progress`)

Per-player, per-game save documents that persist between sessions. Server code uses them through
`PlayerProgress` (`com.ryan.gameframework`, `RyanAssets.Server.ServerCore`); this assembly holds the
engine-agnostic parts:

- `ProgressData`: base class of a game's document. Public fields are saved, properties are not.
  Raise `LatestVersion` and implement `Migrate` when a field changes meaning; `Validate` repairs loaded values.
- `ProgressCodec`: JSON (de)serialization with versioning. No type names are read from saved data.
- `ProgressBook`: the documents of the players on a server: queued changes while loading, dirty
  tracking, ordered saves, and "session only" mode when the store fails, so a hiccup never overwrites real progress.
- Stores: `BackendProgressStore` (UnityBackend `/api/internal/v1/progress`, game servers only) and
  `LocalFileProgressStore` (`Application.persistentDataPath/PlayerProgress/<universe>/<player>.json`,
  used when `LocalNetworkConfig.editor_direct_connection` is on).

```csharp
public sealed class OB_SaveData : ProgressData {
    public const string Universe = "obby";
    public override int LatestVersion => 1;
    public int coursesFinished;
}

// server runner
PlayerProgress.Load<OB_SaveData>(player, OB_SaveData.Universe).Forget();       // on join
PlayerProgress.Modify<OB_SaveData>(connection, save => save.coursesFinished++); // on an event
```

Saves happen every `PlayerProgress.AutosaveIntervalSeconds` (60 s), when a player leaves, and at
shutdown. Documents are capped at 64 KiB. EditMode tests: `Tests/Editor/ProgressTests.cs`.
