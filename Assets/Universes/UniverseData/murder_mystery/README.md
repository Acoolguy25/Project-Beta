# Murder Mystery

Server-authoritative murder mystery on the old mansion map. Mode is picked by a vote at the start of each
round (`MM_VoteMode`): NPCs vs Players, Classic, or Infection.

## Classic round flow

| Phase | Length | What happens |
|---|---|---|
| Role reveal | 6 s | Roles are dealt (`MM_Roles`), everyone is invulnerable, each player sees a private role card. |
| Hunt | 180 s, paused during meetings | Murderers (players or NPCs) kill with daggers, sheriffs (players only) shoot. Bodies stay reportable. Murderers wait out a grace period first and a cooldown after every kill (below). |
| Meeting | 15 s | A living player presses **E** (Interact) within 6 m of an unreported body. Everyone freezes and is invulnerable. The notice names the reporter, the victim, and who was seen near the body. |
| Accusation vote | up to 25 s | Living players vote among the suspects (the 3 living characters closest to the body when it happened, padded with whoever is near it now; every card reads "Seen near the body", so a human suspect is not singled out). Most votes is ejected; a tie or no votes ejects nobody. Ends early once everyone voted. |
| Ejection | 6 s | The ejected character dies and their role is revealed. The hunt resumes with the time it had left. |
| Results | 12 s | "Murderers win" or "The town wins" (sheriffs and innocents win together), with how it was decided underneath ("Every murderer was caught", "The murderers ran out of time", "Nobody was left to stop the murderers"). The outcome travels as a flag (`MM_ResultsBroadcast.murderersWon`), not by reading the headline. Every player's role, fate and kill count plus the NPC murderers and sheriffs are revealed, then the next round starts. An innocent who took a dropped weapon is listed and counted with the role they ended with, so the summary adds up to everyone dealt in. |

Limits: 3 meetings per round, no report in the first 10 s of the hunt or within 20 s of the last meeting.
If every player is eliminated while a murderer is still alive, the murderers win.

All of these numbers live in one serialized block, **Classic Tuning** (`MM_ClassicTuning`) on the
`MM_ServerRunner` component, so they can be tuned in the Inspector without code changes.

### Balance: grace period and kill cooldown

So a lone sheriff is not dead within seconds of the hunt starting (`MM_KillClock`, unit-tested in `Tests/`):

- **Grace period** (25 s): nobody can kill at the start of the hunt.
- **Kill cooldown** (25 s): after a kill, that murderer waits before the next one.
- **Post-meeting cooldown** (10 s): when a meeting ends, every murderer waits at least this long.

The kill cooldown keeps running through meetings (it is wall-clock time, as in Among Us): a murderer who
killed just before a 40 s meeting usually comes out of it with only the 10 s post-meeting wait left. That
is deliberate, so a meeting is never a safe way for a murderer to bank time; the post-meeting cooldown is
the floor.

### Balance: how hard NPC murderers hunt the sheriff

`MM_NPC` lets NPC murderers prefer an armed sheriff over other targets. The **NPC murderers and the
sheriff** block of Classic Tuning sets how hard (`MM_ClassicTuning.SheriffNoticeDistance` combines the
distances, `MM_ClassicRound.SheriffNoticeDistance` feeds it the round's state):

| Field | Default | What it does |
|---|---|---|
| `sheriffNoticeDistance` | 45 m | Before the manhunt, a murderer that may kill only goes for a sheriff this close. |
| `sheriffExposedSeconds`, `exposedSheriffNoticeDistance` | 8 s, 60 m | A sheriff who just fired is noticed from further away. |
| `sheriffManhuntAfterSeconds`, `manhuntNoticeDistance` | 60 s, 1000 m | After this much hunt (meetings not counted) murderers go looking for the sheriff anywhere on the map. |
| `manhuntStaggerSeconds` | 35 s | Each further living murderer joins the manhunt this much later, so they come one at a time. |
| `sheriffCreepDistance`, `sheriffCreepSpeed` | 25 m, 4 m/s | Inside this distance of an armed sheriff a murderer creeps instead of charging (a charge is 20 m/s, 10 m in half a second): the sheriff's chance to see it coming. |
| `sheriffDetectionMultiplier`, `sheriffTargetPriority` | 1.5, 1 | Range multiplier and priority bonus for a sheriff target over a plain innocent. |

Measured on 2026-11-17 at these defaults (solo, the human is the sheriff and never moves, 28 NPC
innocents, 2 NPC murderers, 180 s hunt). "Fight-back" is a server probe standing in for a player: it
reacts 1.5 s after a murderer that is going for the sheriff comes within 20 m, then fires once a second
with a 35 % chance to kill it and a 5 % chance to hit an innocent instead (a real wrong shot: the pistol
jams). It reacts at 20 m rather than 10 m because at 10 m even a creeping murderer strikes before a 1.5 s
reaction ends.

- Fight-back, 5 rounds: the town won 3 (both murderers shot in all 3), the sheriff died at 89 s and 93 s
  in the other two; murderers came for the sheriff 2 times per round on average.
- AFK sheriff, 3 rounds: dead at 71 s, 82 s and 87 s (murderers won all 3), never before the manhunt.

Before (same day): 45 m alone gave about one approach per round and a standing sheriff won 1 in 3; with
the manhunt but no creep the probe never got a shot off (murderers charge 10 m in 0.5 s); with both
murderers joining the manhunt together they arrived as a pair and the town lost 3 of 3.

NPC murderers ask `MM_ClassicRound.CanKill` before every attack and holster while they may not kill (they
wander like townsfolk instead of stalking). A player murderer's dagger is put on the matching server
cooldown, which their client shows and respects, and their role chip counts the seconds down.

### The sheriff's gun drops

When the sheriff dies (shot, stabbed or voted out) their gun drops where they fell, with a beam over it
and a small "SHERIFF'S GUN" label (`gunCalloutSize`, `gunCalloutHeight` on the HUD) for the living to
see. The first living, empty-handed innocent **player** to touch it takes it and secretly becomes the
Sheriff: their takeover card appears at the top of the screen (not over their own character), nobody else
is told. NPCs never pick weapons up. Optionally (`dropDaggerOnMurdererDeath`, **off** by default) a dead
murderer's dagger drops the same way and turns an innocent into a murderer; a murderer the town voted
out never drops one, so a correct vote cannot be undone by whoever walks over the dagger. Dropped weapons
are cleared when the round ends. Rules: `MM_DropRules` (unit-tested).

Editor-only switch **DebugSoloNpcSheriff** on `MM_ServerRunner`: with a single player, the sheriff is
dealt to an NPC (which carries the pistol but does not shoot), so one person can test the gun drop and
takeover as an innocent. It does nothing in builds.

## Presentation and feel (client)

`MM_ClientHUD` reacts to the server's broadcasts with the shared game-feel kit (`RyanAssets.GameFeel`):

| Moment | Feedback |
|---|---|
| Role reveal | Pizzicato sting and a flash in the role's colour; again when a role is taken over mid-round. |
| Kill | Sparks, a shockwave ring under the victim and a kill sting where it happened, camera shake by distance (1.5x for the killer). Only players within earshot (`killAudibleDistance`, 45 m, in Classic Tuning) are told. The victim gets a red flash and "YOU WERE KILLED". |
| Body found | Alarm and horn, red flash and a jolt, then three gavel knocks call the meeting. |
| Accusation vote | A drumroll that speeds up and swells until the result. |
| Ejection | A bell toll, then a win or lose sting and a flash in the outcome's colour. |
| Gun dropped / taken | Beacon beam and a "SHERIFF'S GUN" callout; a pickup sound when someone takes it. |
| Murderer cooldown | Dagger chip on the role chip counts down; a beep when the dagger is ready. |

The HUD also applies the kit's **WarmIndoor** post-processing mood while the game is open. Shot tracers,
impacts and footsteps come from the shared weapon and character code.

Sounds are Kenney CC0 (see `CREDITS.md`) in `Audio/`, wired by the authoring menu.

## Atmosphere

**Ryan > Murder Mystery > Apply Atmosphere** lights `murder_mystery_start.unity` as a house at night with
two hues:

- **Cool moonlight** for everything that is not near a lamp: blue-grey fog and trilight ambient, and the
  directional "Global Visibility Fill" tinted moon blue. It is the main light and the only one that casts
  (soft) shadows, so characters and lamps throw a shadow.
- **Warm tungsten from lamps** spread through the house, so a warm source is in view from anywhere you
  stand: a standing lamp (`Prefabs/MM_StandingLamp.prefab`, KayKit model with an emissive shade
  `Materials/MM_LampGlow.mat`, a point light in the shade, a thin capsule collider and a carving
  NavMeshObstacle) goes against the nearest wall at each ceiling light and room fill, skipping any spot
  within 15 m of a lamp on the same storey. 24 lamps (3 in the Lower Hall, one on the landing at the top of
  the Secret Room stairs, from `ExtraLampAnchors`). Where the wall has room, the lamp comes as
  `Prefabs/MM_LampCorner.prefab`: an armchair, a cabinet and a rug round it (KayKit Furniture Bits, box
  colliders and carving obstacles); 11 corners. A ceiling light with no floor under it uses its nearest
  fill's floor instead.
- The room fill nearest each lamp (within 10 m) is switched off: the lamp replaces it, so the floors do
  not stack more lights against the 8-per-object limit. The other fills are a short-range (13 m), nearly
  white bounce, so pools stay separate and characters keep their own colours; a fill within 3 m of a wall
  is dimmed in proportion (down to 25 %), so it does not paint a hard disc on the wall. The ceiling lights
  are a dim top light (24 m).
- The ambient ground term is a deep blue, so the darkest walls are blue rather than black.

The Upper Hall's floor and walls are inactive in the map (only the balcony edge is visible from the Lower
Hall) and the Secret Room behind the stair landing is not built, so neither gets a room of lamps.

Pipeline limits (shared `StarterAssetsURPAsset`, not changed): Forward, 8 additional lights per object,
no additional-light shadows, so point lights are authored with shadows off. The tool also puts the
"Dense Warm Fill Lights" and "Additional Warm Lights" containers back to an identity transform under
`Map` (they had a transform that left all 56 lights near the world origin). Re-running it is safe and
rebuilds the lamp prefabs and the lamps.

The shade's glow holds because `MM_LampGlow.mat` has the `RealtimeEmissive` GI flag: URP's material
validation turns `_EMISSION` off again (on import, in Play mode, on Editor stop) for a material without an
emissive GI flag, which is what cleared it in earlier passes. `MM_LampGlowTests` checks the asset.

Colour bands in dark gradients on the cloud test machine (olive and red rings round a lamp's pool on a dark
wall) come from the colour-grading pass of the shared mood volume on the software renderer (llvmpipe):
the same frame with the volume off is a smooth gradient. The post-processing look
(`MoodPreset.WarmIndoor`) is applied at runtime by the HUD; judge the result in Play mode.

## Saved progress

`Server/MM_SaveData.cs`: rounds played, Classic wins per role, correct accusation votes, murders,
murderers shot as sheriff, and sheriff guns picked up.

## Code

- `Server/MM_ServerRunner.cs` spawning, roles, tools, win conditions, mode vote; sends each player an `MM_StateBroadcast` twice a second.
- `Server/MM_ClassicRound.cs` the Classic phases above: deaths, body reports, meetings, ejections, results.
- `Shared/MM_Protocol.cs` broadcasts between server and client.
- `Shared/MM_ClassicTuning.cs`, `Shared/MM_KillClock.cs`, `Shared/MM_DropRules.cs` Classic tuning and pure rules (tested in `Tests/`).
- `Client/MM_ClientHUD.cs` role card and badge, report prompt, body markers, meeting notice, ejection banner, results, sounds and effects.
- `Editor/MM_Authoring.cs` (menu **Ryan > Murder Mystery > Rebuild HUD and Markers**) rebuilds `Prefabs/MM_HUD.prefab`
  and `Prefabs/MM_BodyMarker.prefab` and makes sure the HUD is in `murder_mystery_start.unity`. Run it from a client Editor.
  The same menu has **Apply Atmosphere** for the scene.
- Accusations use the shared vote window through `ServerVote.StartDynamicVote` (runtime options, voter filter,
  tie-aware result), so other games can run the same kind of vote.
