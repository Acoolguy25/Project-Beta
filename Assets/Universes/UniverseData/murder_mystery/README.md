# Murder Mystery

Server-authoritative murder mystery on the old mansion map. Mode is picked by a vote at the start of each
round (`MM_VoteMode`): NPCs vs Players, Classic, or Infection.

## Classic round flow

| Phase | Length | What happens |
|---|---|---|
| Role reveal | 6 s | Roles are dealt (`MM_Roles`), everyone is invulnerable, each player sees a private role card. |
| Hunt | 180 s, paused during meetings | Murderers (players or NPCs) kill with daggers, sheriffs (players only) shoot. Bodies stay reportable. Murderers wait out a grace period first and a cooldown after every kill (below). |
| Meeting | 15 s | A living player presses **E** (Interact) within 6 m of an unreported body. Everyone freezes and is invulnerable. The notice names the reporter, the victim, and who was seen near the body. |
| Accusation vote | up to 25 s | Living players vote among the suspects (the 3 living characters closest to the body when it happened, padded with whoever is near it now). Most votes is ejected; a tie or no votes ejects nobody. Ends early once everyone voted. |
| Ejection | 6 s | The ejected character dies and their role is revealed. The hunt resumes with the time it had left. |
| Results | 12 s | "Murderers win" or "The town wins" (sheriffs and innocents win together). Every player's role, fate and kill count plus the NPC murderers and sheriffs are revealed, then the next round starts. An innocent who took a dropped weapon is listed and counted with the role they ended with, so the summary adds up to everyone dealt in. |

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
sheriff** block of Classic Tuning sets how hard:

- `sheriffNoticeDistance` (45 m): an NPC murderer only goes for a sheriff it can see this close.
- `sheriffExposedSeconds` (8 s) and `exposedSheriffNoticeDistance` (60 m): a sheriff who just fired is
  exposed, and murderers notice them from much further for that long.
- `sheriffDetectionMultiplier` (1.5) and `sheriffTargetPriority` (1): range multiplier and priority bonus
  for a sheriff target over a plain innocent.

Measured on 2026-11-16 (solo, the human is the sheriff, 28 NPC innocents, 2 NPC murderers), 45 m:
an AFK sheriff died at 144 s and 86 s and survived once (2-1 murderers; at 100 m they died within 30-55 s);
a sheriff who shoots back at murderers coming for them (server probe, 50 % hit chance inside 10 m) won
5 of 5. At 12 m no murderer ever came for a sheriff who stood still. A near-even fight-back split is not
reached yet; `sheriffExposedSeconds` (so the second murderer comes after a shot) is the next knob.

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
- **Warm tungsten only from lamps**: a standing lamp (`Prefabs/MM_StandingLamp.prefab`, KayKit model with
  an emissive shade `Materials/MM_LampGlow.mat` and a point light in the shade) is placed against the
  nearest wall under each ceiling light, under `MM_Lamps` in the scene. Seven rooms get one; the Upper
  Hall and Secret Room have no floor under their ceiling light and are lit by the fills.
- The 56 room fills are a short-range (13 m), nearly white bounce, so light pools stay separate and
  characters keep their own colours; the ceiling lights are a dim top light (24 m).

Pipeline limits (shared `StarterAssetsURPAsset`, not changed): Forward, 8 additional lights per object,
no additional-light shadows, so point lights are authored with shadows off. The tool also puts the
"Dense Warm Fill Lights" and "Additional Warm Lights" containers back to an identity transform under
`Map` (they had a transform that left all 56 lights near the world origin). Re-running it is safe and
rebuilds the lamp prefab and the lamps. Known issue: in Editor Play mode the client clears the shade
material's `_EMISSION` keyword about 15 s after the map loads (not from this game's code, cause not found
yet), so the shade is then lit by its bulb only; if the Editor saves assets afterwards the .mat loses the
keyword too, and re-running Apply Atmosphere restores it. The post-processing look (`MoodPreset.WarmIndoor`) is applied at
runtime by the HUD; judge the result in Play mode.

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
