# Investigator

A cooperative, two-chapter horror investigation set in Flooded Grounds' abandoned waterside settlement. The server generates a coherent new case for every round and owns progression, interaction validation, monster behavior, timers, recovery limits, and endings.

## Play

Launch the existing client from `Assets/Scenes/MainMenu.unity` and join **Investigator** through the game browser. Server builds use `-universe_id=classic_horror` through the existing hosting system. Both horror scenes are included in Editor build settings.

For the existing server Editor/ParrelSync workflow, use **Ryan > Server > Editor Universe**, save `classic_horror`, then play `Assets/Scenes/ServerInit.unity` in the server Editor. Play MainMenu in the client Editor. Normal joins still use the project's existing authentication backend. Opening the map alone previews the environment; it does not start an offline game.

| Input | Action |
| --- | --- |
| WASD / mouse | Move / look |
| Shift | Sprint, consuming existing stamina |
| Left click | Toggle equipped flashlight |
| E | Investigate the object under the reticle |
| F | Open/close the field journal |
| E at an offering beside the source | Offer salt / bell / lantern |
| Escape in journal | Close journal |

The game locks the camera to first person. The journal releases the cursor and disables movement/tool input while open; the networked world keeps moving. New investigators receive the team's current evidence and objective.

## Two chapters

**I — The Last Call:** follow the search bearing, investigate four records, and discover who vanished, what woke the presence, how it hunts, and where its source lies. Two optional memories preserve the lost people's names.

**II — What Answered:** recover the salt, bell, and lantern from newly revealed locations. Reach the source and offer them in the order recorded in this case's journal. A mistake resets the ritual and enrages the monster. Once the seal holds, return to the arrival radio within two minutes. Recovering both optional memories earns the fuller ending.

Each investigator has three revives per case. A death consumes one revive and returns that investigator after five seconds, including when the last revive is consumed. Only a subsequent death with no revives remaining enters spectate. Evidence survives a death; the case fails when every investigator is eliminated. Successful and failed rounds automatically restart after 18 seconds with a new seed. Chapter limits default to 12 and 9 minutes; tune them on `Server/CH_ServerRunner.prefab`.

## Replayability and authoring

Each case selects a witness, missing person, cause, dialog variants, one of three source locations, nine distinct sockets from sixteen search locations, one of six ritual orders, two optional memories, warning order, and one of three monster temperaments:

- **Light seeker:** sees lit investigators from much farther away; extinguish the light and use cover.
- **Light shy:** a directly aimed beam drives it to retreat while sight is clear.
- **Listener:** hears sprinting through nearby cover; walk quietly and break sight.

The case uses authored, composable story templates in `Data/StoryLibrary.asset`, not an external language-model service. A seed guarantees reproducibility; finite combinations can recur. `fixedSeed = 0` chooses fresh seeds; set a nonzero seed on the server runner to reproduce a case while authoring. Keep `{witness}`, `{missing}`, `{cause}`, and ritual/source tokens in templates to preserve consistency.

`CH_Map` holds the arrival, extraction, monster spawn, search sockets, source sockets, presentation references, and practical lights. Its `NavMeshSurface` is baked into `Scenes/InvestigationNavigation.asset`; deep water is excluded. Re-bake and check complete paths when moving sockets or changing collision geometry. Players use scale 1.05 and the monster 1.1. The dedicated HorrorPresence navigation agent has radius 0.3 and height 2.05. Pursuit requires a complete path to the actual player elevation; unreachable or stalled pursuits are abandoned temporarily. Patrol and retreat continue moving.

**Ryan > Classic Horror > Rebuild Presentation Prefabs** rebuilds the HUD, flashlight, presence, and evidence presentation. It preserves authored map layout and an existing story library. The shared Monster prefab reuses RobotNPC's networked character, animator, ragdoll, health, and LocalNPC movement. `ServerNPC.SpawnNPC(NPCCharacter.Monster, ...)` selects it through `NPCCharacters`; omitted character selection defaults to Robot. The monster and jumpscare portrait use the same PresenceFace asset, bound to the animated humanoid head.

## Shared additions

Reusable features in `Assets/RyanAssets` include first-person camera mode and server camera lock, first-person tool view, a networked flashlight integrated with the existing tool/stamina lifecycle, utility ToolControls actions, seeded story randomness, occluded world interaction checks, externally directed LocalNPC movement, server Editor universe selection, a folder-scoped legacy material upgrader, and a URP water shader.

The Flooded Grounds materials use URP Lit or the reusable water shader. The converted Tree Creator prefabs retain their embedded meshes/materials, recovered through Unity's asset API. The horror map replaces the demo camera/input UI and old post processing with the existing client and a URP volume; stale baked occlusion and lighting data were cleared. Mirrored box colliders were replaced with convex mesh colliders, and legacy terrain trees use regular LOD renderers on a private terrain copy. The map uses dedicated realtime lighting settings and never bakes lighting on scene load; **Ryan > Classic Horror > Configure Realtime Map Lighting** reapplies this configuration without saving temporary network scenes.

## Validation

Five Editor tests pass, covering deterministic generation, 512 solvable seeds with all monster/source/order variants, optional evidence, invalid progression, and map preconditions. The authored navigation was checked for complete paths to all 21 arrival/search/source/monster anchors.

The current client/server Editor playtest passed mouse look, left-click flashlight toggling (including sky aim), F journal open/close and input gating, E interactions for all evidence and offerings, the ritual, extraction, and THE WATER REMEMBERS ending. Input checks also verified first-person lock, smaller player scale, and no monster nametag. The test repositioned the client between objectives and repelled the monster to isolate progression; it was not a full walking/balance playthrough. All 26 objective positions have reachable ground approaches in the geometry audit.

Gameplay testing used an in-memory, Editor-only loopback identity, removed on exiting Play mode. Normal joins retain the existing authentication backend.
