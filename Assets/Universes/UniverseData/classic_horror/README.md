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

Each investigator has four lives per case, counting the spawn they start the case with. A death spends one life and returns that investigator after five seconds; spending the last life eliminates them into spectate for the rest of the case. Lives and eliminations are tracked per account, so leaving and rejoining mid-case does not refill them. The team arrives together, spread around the arrival jetty. A recovered or late-joining investigator regroups at the authored location nearest a living teammate, skipping locations within 25 m of the Presence (`spawnMonsterSafeDistance`). A spawn point must be on the navigation mesh, above the map's `waterLevel`, on level ground with a full investigator-sized capsule of headroom, and clear of other investigators; the Presence's tiny-agent navmesh alone also covers crates, rails and crawl spaces. Evidence survives a death; the case fails when every investigator is eliminated. Successful and failed rounds automatically restart after 18 seconds with a new seed. Chapter limits default to 12 and 9 minutes; tune them on `Server/CH_ServerRunner.prefab`.

## Story

Beyond the dispatch introduction and the records themselves, the radio carries the case's story:

- **Witness replies** after each of the first three required records, and the final record stays readable before Chapter II's transmission plays.
- **Story beats:** an unknown voice about a minute in (`earlyStoryBeatSeconds`), the chapel bell and the missing person's whisper once two records are found (the Presence briefly hunts harder), the priest's invitation 25 seconds into Chapter II, and the missing person begging during the escape.
- **The ritual answers:** each accepted offering has its own line, and a wrong order brings a taunt from the priest or a panicked cry from the missing person.
- **The priest's diary:** three optional pages (ids 14 to 16) in otherwise unused search locations tell how the chapel priest (one of four seeded names) bargained names to the water and became the Cursed Priest. Reading a page makes the Presence falter for six seconds; finishing the diary makes him show himself to the reader once.
- **Five endings:** LAST RITES (both memories and the whole diary), THE WATER REMEMBERS (both memories), A NAME FOR A NAME (whole diary), BORROWED SILENCE (neither), and STILL MISSING on failure. Endings name the case's witness, missing person and priest, and the ending card adds the case in numbers: evidence, memories, diary pages, the Presence's temperament, ritual mistakes, lives lost and time.

Every line is written "SPEAKER / words": the radio card shows the speaker as its caption and types the words out after a burst of static; the journal shows it as the page heading. Radio lines queue so a new line never cuts off the previous one within nine seconds, and the Presence's warnings are dropped rather than interrupting a story line.

All story text lives in `CH_StoryLibrary` (cast, cause, dispatch, confessions, ritual instructions, memories, diary, replies, beats, offering and mistake lines, ambient-scare reactions, chapter II transmissions and the endings). `Data/StoryLibrary.asset` keeps whatever it was saved with; run **Ryan > Classic Horror > Reset Story Library To Authored Text** after changing the defaults in code.

## Fear, scares and atmosphere

- **Fear loop (client):** a heartbeat that quickens from 62 to 142 bpm, ragged breathing, a low drone and a red screen edge (the shared `ScreenFx.SetDanger` vignette, pulsing with the heartbeat and drawn under the HUD) as the Presence closes in (42 m to 7 m), the map's lamps and the investigator's own flashlight stuttering when it is close, and a sting and camera jolt when the server switches to the chase music (the kit crossfades the tracks). Fear rises quickly and settles slowly. A surface right in front of the flashlight dims the beam (`closeRangeDistance`), so a wall at arm's length is not blown out. Tune it on `CH_FearLoop` and `CH_Atmosphere`.
- **Encounter scares:** the priest's face (`Data/EncounterFace.png`, rendered by the HUD rebuild: the face cropped under the chin, lit hard from below over a red glow, on a canvas above the shared HUD), the sting, a camera jolt and a burst of chromatic aberration, red vignette and lens distortion (`Data/EncounterLens.asset`) that holds while the face is up and eases away over the next moment (`lensRecoveryFraction`), so the player sees the world warped as it returns. Warning (face to face within `proximityScareDistance`) and discovery (wrong offering, finishing the diary) scares are limited to `nonFatalScaresPerInvestigator` per case with `nonFatalScareCooldown` between them; the fatal scare always plays, and a warning never fires inside the Presence's strike range, where the fatal scare would land on top of it. A warning or discovery scare also holds the Presence in place (`warningStaggerSeconds`, `discoveryStaggerSeconds` on the runner) for as long as the face is up, so the player can still run when it fades. The rules live in `CH_ScareRules` and are tested.
- **The Presence's tuning:** every hunt number (speeds per chapter and when enraged, the lone-investigator cap, pursuit memory, strike distance, reach and interval, whispers) is in `CH_MonsterTuning`, serialized on the runner as `monsterTuning`, beside the enrage and repel durations. A single living investigator is never hunted faster than `loneInvestigatorSpeedCap` (6.5 m/s, between the 4.5 m/s walk and the 9.5 m/s sprint) and is forgotten after `lonePursuitMemorySeconds` (8 s) out of sight, so breaking line of sight works alone. The runner holds the Presence back for `openingGraceSeconds` (45 s) at the start of a case and for `caughtRepelSeconds` (20 s) after a catch.
- **Ambient scares (server-timed, everyone hears them):** a door slamming at a search location 10 to 28 m from someone, a cry from the location furthest from the team, every lamp failing for three seconds, the chapel bell at the source. Every 70 to 140 seconds after the first minute, never during a chase; the radio sometimes reacts.
- **Evidence:** records breathe an amber rim glow (`Materials/CH_EvidenceRim.shader`) and brighten under the reticle; securing one bursts into sparks for everyone, the collector gets the sting and a small jolt, and the journal announces the new page with a sound, a "New entry" badge and a punch on the F hint. The journal opens on the newest unread page.
- **Atmosphere:** `CH_Atmosphere` applies the night lighting (`CH_NightLighting`: moon, ambient, fog, night sky; the same values **Configure Realtime Map Lighting** writes into the scene), the shared Horror mood from the game-feel kit (grain, vignette, desaturation), and lit dust motes that only show inside the flashlight beam. The map's own night grade (`Data/HorrorAtmosphere.asset`, volume priority 25) sits on top of the kit mood with neutral tonemapping and positive exposure, so the scene is dark but readable rather than crushed. Each practical light hangs in a KayKit lantern post (`Prefabs/SearchLantern.prefab`) whose glass glows and flickers with the light. Investigators spawn with the flashlight on (`flashlightStartsOn`).

## Replayability and authoring

Each case selects a witness, missing person, cause, dialog variants, one of three source locations, twelve distinct sockets from sixteen search locations (nine for the case, three for the diary), one of six ritual orders, two optional memories, warning order, and a priest, and one of three monster temperaments:

- **Light seeker:** sees lit investigators from much farther away; extinguish the light and use cover.
- **Light shy:** a directly aimed beam drives it to retreat while sight is clear.
- **Listener:** hears sprinting through nearby cover; walk quietly and break sight.

The case uses authored, composable story templates in `Data/StoryLibrary.asset`, not an external language-model service. A seed guarantees reproducibility; finite combinations can recur. `fixedSeed = 0` chooses fresh seeds; set a nonzero seed on the server runner to reproduce a case while authoring. Keep `{witness}`, `{missing}`, `{cause}`, and ritual/source tokens in templates to preserve consistency.

`CH_Map` holds the arrival, extraction, monster spawn, search sockets, source sockets, presentation references, and practical lights. Its `NavMeshSurface` is baked into `Scenes/classic_horror_start/NavMesh-Investigation.asset`; deep water is excluded. Re-bake and check complete paths when moving sockets or changing collision geometry. Players use scale 1.05 and the monster 1.1. The dedicated HorrorPresence navigation agent intentionally uses a tiny radius and clearance capsule (radius 0.1, height 1.05, step height 1.0) so it can cross raised foundations and narrow house entrances. The physical root hitbox is similarly reduced. Once a pursuit starts, the monster tracks the investigator through cover for 20 seconds before returning to patrol.

**Ryan > Classic Horror > Rebuild Investigation HUD** rebuilds only this game's HUD prefab with its presenters, sounds, dust and lens assets. **Ryan > Classic Horror > Rebuild Presentation Prefabs** rebuilds the HUD, flashlight, priest, and evidence presentation (the flashlight and priest live in the shared gameframework package). It preserves authored map layout and an existing story library. The shared Monster prefab uses the Cursed Priest's original weighted Humanoid skeleton and avatar with RobotNPC's exact controller, walking/jumping clips, network animation, health, and LocalNPC movement. It has one CharacterAnimator driver and project-owned URP materials. `ServerNPC.SpawnNPC(NPCCharacter.Monster, ...)` selects it through `NPCCharacters`; omitted character selection defaults to Robot. PresenceFace and the jumpscare portrait are generated from the same priest head and hat.

**Ryan > Classic Horror > Rebuild Evidence Prefabs** refreshes only the evidence table and haunting altar. They retain linked Flooded Grounds furniture and use project-owned URP materials for imported portal and spectral electricity effects; their imported colliders are disabled so investigation rays still reach the case interactions.

A successful priest melee hit consumes the investigator's remaining health through the shared damage API, respecting spawn invulnerability. Nonfatal scares are limited to two per investigator per case, with a 90-second cooldown shared by proximity and discovery/ritual encounters. Proximity scares require visibility within six units. Priest kills can always show a fatal scare; environmental deaths do not show the priest. Warning, discovery, and fatal scares last 2.25, 1.75, and 3.5 seconds respectively (`CH_Jumpscare.encounters`), with a single appearance and gradual fade.

**Ryan > Classic Horror > Diagnostics > Capture Lighting Probe** renders the map from the arrival jetty in edit mode (no server, no Play mode), with and without post-processing, writes `Logs/ch-lighting-probe-*.png` and logs the GPU, the active scene's RenderSettings and the linear brightness of sky, horizon and ground. It also runs in batchmode with a display.

## Shared additions

Reusable features in the shared packages (`Packages/com.ryan.*`) include first-person camera mode and server camera lock, first-person tool view, a networked flashlight integrated with the existing tool/stamina lifecycle, utility ToolControls actions, seeded story randomness, occluded world interaction checks, externally directed LocalNPC movement, server Editor universe selection, a folder-scoped legacy material upgrader, and a URP water shader.

The Flooded Grounds materials use URP Lit or the reusable water shader. The converted Tree Creator prefabs retain their embedded meshes/materials, recovered through Unity's asset API. The horror map replaces the demo camera/input UI and old post processing with the existing client and a URP volume; stale baked occlusion and lighting data were cleared. Mirrored box colliders were replaced with convex mesh colliders, and legacy terrain trees use regular LOD renderers on a private terrain copy. The map uses dedicated realtime lighting settings and never bakes lighting on scene load; **Ryan > Classic Horror > Configure Realtime Map Lighting** reapplies this configuration without saving temporary network scenes. It also places the lantern posts, writes the night grade, and points the scene's falling-leaf, dust and halo particles at copies in `Materials/Particles/` that use the pack's `_Transparent.png` textures and plain alpha blending (the pack originals drew as lit squares under the flashlight).

## Validation

Sixteen Editor tests cover deterministic generation, 512 solvable seeds with all monster/source/order variants, optional evidence, invalid progression, fully expanded story text, layout stability when story text changes, endings, map preconditions, the encounter-scare rules (no warning inside strike range, sight and range, per-case rationing with a cooldown) and the Presence's tuning (a lone investigator can outrun every hunt and shakes it off sooner). The authored navigation was checked for complete paths to all 21 arrival/search/source/monster anchors.

The client/server Editor playtest (headless server, about 3 fps llvmpipe client) passed mouse look, left-click flashlight toggling, the journal, E interactions for every record and offering, the ritual, extraction and the BORROWED SILENCE ending ("Cases solved" one higher, save `casesCompleted` +1). That win used teleports between objectives and repelled the Presence between pickups. Balance was checked separately with an on-foot bot (real movement input, NavMesh routes, sprinting away or holding the beam on a light-shy Presence) against an active Presence: with the current tuning the bot finished Chapter I in two of four solo cases (fastest 119 s) but never won; the trace shows its deaths come from dead ends while fleeing, not from the Presence out-running a sprint. Warnings never stacked on a catch, every catch showed a single fatal face, and a player who ran after a warning lived 16 to 24 s longer.

Gameplay testing used an in-memory, Editor-only loopback identity, removed on exiting Play mode. Normal joins retain the existing authentication backend.
