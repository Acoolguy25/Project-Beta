# Murder Mystery

Server-authoritative murder mystery on the old mansion map. Mode is picked by a vote at the start of each
round (`MM_VoteMode`): NPCs vs Players, Classic, or Infection.

## Classic round flow

| Phase | Length | What happens |
|---|---|---|
| Role reveal | 6 s | Roles are dealt (`MM_Roles`), everyone is invulnerable, each player sees a private role card. |
| Hunt | 180 s, paused during meetings | Murderers (players or NPCs) kill with daggers, sheriffs (players only) shoot. Bodies stay reportable. |
| Meeting | 15 s | A living player presses **E** (Interact) within 6 m of an unreported body. Everyone freezes and is invulnerable. The notice names the reporter, the victim, and who was seen near the body. |
| Accusation vote | up to 25 s | Living players vote among the suspects (the 3 living characters closest to the body when it happened, padded with whoever is near it now). Most votes is ejected; a tie or no votes ejects nobody. Ends early once everyone voted. |
| Ejection | 6 s | The ejected character dies and their role is revealed. The hunt resumes with the time it had left. |
| Results | 12 s | Every player's role, fate and kill count plus the NPC murderers are revealed, then the next round starts. |

Limits: 3 meetings per round, no report in the first 10 s of the hunt or within 20 s of the last meeting.
If every player is eliminated while a murderer is still alive, the murderers win.

## Code

- `Server/MM_ServerRunner.cs` spawning, roles, tools, win conditions, mode vote; sends each player an `MM_StateBroadcast` twice a second.
- `Server/MM_ClassicRound.cs` the Classic phases above: deaths, body reports, meetings, ejections, results.
- `Shared/MM_Protocol.cs` broadcasts between server and client.
- `Client/MM_ClientHUD.cs` role card and badge, report prompt, body markers, meeting notice, ejection banner, results.
- `Editor/MM_Authoring.cs` (menu **Ryan > Murder Mystery > Rebuild HUD and Markers**) rebuilds `Prefabs/MM_HUD.prefab`
  and `Prefabs/MM_BodyMarker.prefab` and makes sure the HUD is in `murder_mystery_start.unity`. Run it from a client Editor.
- Accusations use the shared vote window through `ServerVote.StartDynamicVote` (runtime options, voter filter,
  tie-aware result), so other games can run the same kind of vote.
