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
| Results | 12 s | Every player's role, fate and kill count plus the NPC murderers are revealed, then the next round starts. |

Limits: 3 meetings per round, no report in the first 10 s of the hunt or within 20 s of the last meeting.
If every player is eliminated while a murderer is still alive, the murderers win.

All of these numbers live in one serialized block, **Classic Tuning** (`MM_ClassicTuning`) on the
`MM_ServerRunner` component, so they can be tuned in the Inspector without code changes.

### Balance: grace period and kill cooldown

So a lone sheriff is not dead within seconds of the hunt starting (`MM_KillClock`, unit-tested in `Tests/`):

- **Grace period** (25 s): nobody can kill at the start of the hunt.
- **Kill cooldown** (25 s): after a kill, that murderer waits before the next one.
- **Post-meeting cooldown** (10 s): when a meeting ends, every murderer waits at least this long.

NPC murderers ask `MM_ClassicRound.CanKill` before every attack and holster while they may not kill (they
wander like townsfolk instead of stalking). A player murderer's dagger is put on the matching server
cooldown, which their client shows and respects, and their role chip counts the seconds down.

### The sheriff's gun drops

When the sheriff dies (shot, stabbed or voted out) their gun drops where they fell, with a beam over it
for everyone to see. The first living, empty-handed innocent **player** to touch it takes it and secretly
becomes the Sheriff (their role card says so; nobody else is told). Optionally (`dropDaggerOnMurdererDeath`,
on by default) a dead murderer's dagger drops the same way and turns an innocent into a murderer. Dropped
weapons are cleared when the round ends. Rules: `MM_DropRules`.

## Presentation and feel (client)

`MM_ClientHUD` reacts to the server's broadcasts with the shared game-feel kit (`RyanAssets.GameFeel`):

| Moment | Feedback |
|---|---|
| Role reveal | Pizzicato sting and a flash in the role's colour; again when a role is taken over mid-round. |
| Kill | Sparks and a kill sting where it happened, camera shake by distance. Only players within earshot (`killAudibleDistance`, 45 m) are told. The victim gets a red flash and "YOU WERE KILLED". |
| Body found | Alarm and horn, red flash and a jolt, then three gavel knocks call the meeting. |
| Accusation vote | A drumroll that speeds up and swells until the result. |
| Ejection | A bell toll, then a win or lose sting and a flash in the outcome's colour. |
| Gun dropped / taken | Beacon beam and a "SHERIFF'S GUN" callout; a pickup sound when someone takes it. |
| Murderer cooldown | Dagger chip on the role chip counts down; a beep when the dagger is ready. |

The HUD also applies the kit's **WarmIndoor** post-processing mood while the game is open. Shot tracers,
impacts and footsteps come from the shared weapon and character code.

Sounds are Kenney CC0 (see `CREDITS.md`) in `Audio/`, wired by the authoring menu.

## Atmosphere

**Ryan > Murder Mystery > Apply Atmosphere** sets warm dark fog and ambient light in
`murder_mystery_start.unity`, turns the fill and ceiling lights tungsten (ceiling lights cast soft
shadows) and dims the white "Global Visibility Fill" to faint warm moonlight. It also puts the
"Dense Warm Fill Lights" and "Additional Warm Lights" containers back to an identity transform under
`Map`: they had a transform cancelling the Map's offset and 3x scale, which left all 56 room lights near
the world origin, outside the house. Re-running it is safe. The post-processing look
(`MoodPreset.WarmIndoor`) is applied at runtime by the HUD; judge the result in Play mode (ACES tonemapping).

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
