# Live probes

The staging aids behind the Classic Horror live traces: shell helpers that evaluate C# in a running server Editor and a
running client Editor through the Unity CLI (`unity command ... script eval`), and the probes they evaluate. Unity skips
this folder (its name ends in `~`), so none of it is compiled into the game. Everything here is staging: it reaches into
private fields by reflection to set up a moment, and never ships.

## Setup

- The main project is the server; its ParrelSync clone `<project>_clone_0` is the client. `env.sh` finds both (override
  `SERVER_PROJECT`, `CLIENT_PROJECT`, `OUT`, `CLIENT_DISPLAY`) and `UNITY_EDITOR` must point at the Editor binary.
- `launch.sh` starts both Editors (the client on a virtual display), `start.sh` puts the server in play, joins with the
  client and waits for a night, and `waitphase.sh Stranded` waits for Act I.
- In a software-rendered container the client renders at about 1 frame a second, which starves the bot. Switch the
  client's cameras off for a trace (`cli.sh -e 'foreach (var c in Camera.allCameras) c.enabled = false; return "off";'`);
  `cli_run.tpl` then turns the body itself, since movement falls back to the body's facing without a main camera. Turn them
  back on for screenshots (`shot.sh name`).

## Probes

| Probe | Editor | What it does |
| --- | --- | --- |
| `srv_trace.tpl LOG=path` | server | Every 0.5 s logs Crane's mode, position and speed, and the gap, path gap and line of sight to the nearest investigator. `LOG=stop` ends it. |
| `cli_run.tpl GX GY GZ AGAIN DROPAT WALKAFTER LOG` | client | The local investigator runs a NavMesh path to the goal like a player: sprints until empty, walks until `AGAIN`% stamina, drops the carried item when he is within `DROPAT` m, goes quiet for good after `WALKAFTER` s. `GX=stop` ends it. |
| `cli_tp.tpl PX PY PZ YAW` | client | Puts the investigator somewhere, facing a yaw. Wait for a respawn to finish first. |
| `cli_e.cs` | client | Presses E (interact) through the controller. |
| `srv_hunt.tpl PX PY PZ` | server | Warps Crane there and sets him on the nearest investigator. |
| `srv_hunt_behind.tpl AX AZ DIST` | server | The same, `DIST` m behind the investigator, away from (`AX`, `AZ`). |
| `srv_hide.tpl DELAY` | server | Toggles the nearest hiding place for the investigator (`DELAY > 0` waits until Crane has seen them, then that long). |
| `srv_corner.cs` | server | Finds chase set-ups: a spot, Crane 15 to 20 m off in plain sight, and a route that ducks out of his sight. |
| `srv_board.cs` | server | Opens the boarding countdown now, shortened to 30 s. |
| `srv_scene.tpl PHASE` | server | Replays a cutscene through the runner's own `PlayCutscene`. |
| `srv_hold.cs` / `srv_release.cs` | server | Suspends Crane where he stands (he can hang mid-stride) and lets him go. |
| `srv_lives.tpl LIVES=n`, `srv_state.cs` | server | Tops lives up to `n`; prints the phase, items, Crane and players. |

| `cli_engine_bot.cs` | client | Opens the engine and pulls the cord as the needle crosses the green, using the panel's own state. |
| `cli_hymn_bot.cs`, `cli_wrong.cs` | client | Sings the hymn the panel was sent (or a wrong note on purpose). |
| `cli_lock_bot.cs` | client | Feels round the bell's dial with probes, then sets each ring at the warmest angle. |
| `cli_hide.cs` | client | Climbs into the nearest hiding place through the controller. |
| `srv_relics.cs`, `srv_fit.cs` | server | Lays tonight's relics on their graves; fits the boat's parts. |
| `srv_place_seen.tpl`, `srv_place_unseen.tpl` | server | Places Crane so he does or does not see the investigator climb in, for the hiding trace. |

`corner_chase.sh` replays the try-9 sight-break run end to end.

## The night bot (try 11)

| Probe | Editor | What it does |
| --- | --- | --- |
| `cli_night_bot.tpl LOG MISS_STRIKE MISS_NOTE MISS_LOCK HIDE_RANGE HIDE_SEARCH` | client | Plays a whole night (the boat parts and relics may be laid by `srv_fit` / `srv_relics` and lives raised by `srv_lives`; the report says which were used): walks the NavMesh through the movement controls, aims by turning the first-person yaw and pitch, presses E through the controller's own Interact, hides when Crane comes within `HIDE_RANGE` m, and works the minigames through the panel's entry points that the keys call: `Sing` (G H J K), `TurnDial` (A / D) and `Action` (E: the cord, the lock's Set). It reads only the server's broadcast. `MISS_*` make that many deliberate mistakes first (a missed strike, a wrong note, a wrong lock). |
| `cli_prep.cs` | client | Draws the client camera into a tiny texture (not needed on a headless client). |

The graphical client Editor is OOM-killed beside the server in this container, so the client is the ParrelSync clone started with `-batchmode -nographics`; the bot does not need a screen. A batchmode Editor has no focused window, so Unity drops keyboard events and no key can be pressed: walking and aiming write the controls directly and the minigames call the panel's own `Sing`, `TurnDial` and `Action`.
