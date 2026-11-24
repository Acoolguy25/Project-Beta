# The Drowned Choir

A cooperative first-person horror night for one to four players in Flooded Grounds' drowned village of Hollowmere. You and Mara came out by ferry to look for her missing brother; the ferry is wrecked, and Father Silas Crane, the priest who drowned his own congregation in the 1962 flood, is still collecting voices for his choir. The server owns the night: its acts, every item, the priest's hunt, the scares, the voice lines and the endings.

## Play

Launch the client from `Assets/Scenes/MainMenu.unity` and join **The Drowned Choir** in the game browser. Server builds use `-universe_id=classic_horror`. In the Editor, pick `classic_horror` in **Ryan > Server > Editor Universe**, play `Assets/Scenes/ServerInit.unity` in the server Editor (the ParrelSync clone) and MainMenu in the client Editor.

| Input | Action |
| --- | --- |
| WASD / mouse | Move / look (first person) |
| Shift | Sprint (shared stamina) |
| Left click | Flashlight on or off |
| E | Pick up or place the item under the reticle; climb into or out of a wardrobe |
| R | Put down what you carry, where you stand (a teammate can pick it up) |
| F | Journal: everything said tonight, newest first |

## The night

1. **Prologue (cutscene, 37 s).** The wreck, Mara at the tiller, the chapel bell, the first sight of Crane. When everyone present has seen a night through before, the short **Arrival** scene (19 s) plays instead. The night waits (up to `loadWaitSeconds`) until every client has the map loaded, so nobody joins the opening half-way.
2. **Act I, Stranded.** Find the **fuel can**, the **ferryman's toolbox** and the **ferry lantern**, carry them one at a time to the boat moored at the jetty on the village's south-west shore (you start there, with the wreck out on the lake) and fit each in its place. Items spawn at least 30 m from the jetty.
3. **The Taking (cutscene, 29 s).** The boat is fixed; Mara walks to the end of the jetty, Crane rises waist deep out of the open lake in front of her (filmed over her shoulder, with the lake and its mist behind him), lunges onto the planks and drags her under. She keeps talking over her radio from the chapel.
4. **Act II, The Choir.** Three of the five drowned singers (Agnes Pell, Thomas Hale, Ruth Ellery, Elias Grey, Margaret Lowe; chosen by the night's seed) have left a relic out: a hymnal, a photograph or a vigil candle, each named for its owner. Lay each on its owner's grave; the other two graves stay closed. Each freed singer says goodbye.
5. **The Chapel Opens (cutscene, 10 s).** The doors open and Crane's bell is on the altar.
6. **Act III, The Last Sermon.** Take the bell and carry it to the boat. When it leaves the altar he rises on the chapel floor behind it (the nearest spot with a whole path to the thief: the altar's top and back panel carry NavMesh islands of their own), stares for 3 s, then follows. It rings while you carry it: Crane hears it across the map (80 m), chases its carrier faster than anyone else, and the carrier cannot hide.
7. **Boarding (40 s).** Everyone still alive gets to the jetty before the boat leaves (it leaves at once when everyone is aboard). Players out of lives are not left behind. Then the **Departure** cutscene (the ferry turns for the open lake and sails, Mara at the tiller, while Crane watches from the end of the jetty) and the ending.

**Lily's music box** is the secret. It sits in one of five bedrooms with no objective arrow. Lay it on Lily's little marker by the jetty to earn the true ending.

**Endings:** *She came home* (escaped with the music box laid), *You escaped* (escaped without it), *The choir sings on* (everyone caught, or the night ran out after 40 minutes). The next night starts 20 seconds after the ending card with a new seed.

**Items never disappear.** You carry one at a time, in your hands, and everyone sees it. E on another item swaps the two, leaving yours where the new one was; R puts yours down where you stand. Dying drops what you carried where you fell; an item lost in the lake washes up where its carrier last stood on dry ground (or where it was found), never nearer the boat, so the water is never a shortcut (`CH_Night.DropPosition`). A placed item stays placed. Boat parts and the bell are reached from the jetty at a point above the gunwale (`CH_Night.SlotReachPoint`). The fuel can is too heavy to run with (4.2 m/s); everything else can be carried at a sprint.

**Lives:** three per player per night, tracked per account so rejoining does not refill them. Deep water pulls you out without costing a life, unless you were carrying something or being chased (`CH_Night.DrowningCostsLife`). A player who ran out of lives comes back with one life when the next act begins (`returnTheLostEachAct`).

## Father Crane

All of his numbers live in `CH_MonsterTuning`, serialized on the runner (`Server/CH_ServerRunner.prefab`); `CH_Monster` reads everything from there and the tests check the same fields.

- **Senses:** a 130° sight cone, 55 m on a lit player and 22 m on one with the flashlight off, 65 m once he is chasing. He hears sprinting through walls at 30 m and walking at 11 m; standing still is silent. Within 3.5 m he senses you whatever you do.
- **Chase:** 6.9, 7.4 and 8.0 m/s in acts I to III (players walk at 4.5 and sprint at 9.5), 8.4 m/s after the bell, 8% slower when only one player is alive. Stamina (100) drains at 10/s while sprinting and comes back at only 12/s after 0.6 s, so a first sprint opens about 30 m but a 300 m run in the open never shakes him off: in Acts II and III he runs a team down within about 400 m. The village's corners cost him more than they cost a runner (about a fifth of his pace in a live trace), so he **closes in**: from 20 m behind he presses harder, up to 1.3 times his pace at 40 m, never faster than a sprinter and never after the bell, which he already chases flat out (`CH_MonsterTuning.ClosingPace`). A long chase stays at 10 to 25 m instead of drifting out of his sight. Break his line of sight, get out of earshot or hide. He aims 0.45 s ahead of where you are running and repaths every 0.2 s. A new path only replaces his current one when it is complete and ends on your floor (`CH_Monster.Steer`); if the point ahead of you cannot be reached he paths to where you are instead, so a repath never stops him in his tracks. He lunges at 11.5 m/s for half a second inside 5.5 m with a clear line.
- **Losing you:** he keeps going where you were heading for 5 s, then sweeps a 14 m area for 14 s. Passing an empty wardrobe on a sweep he may rattle it (25%, once each): a scare for anyone hiding nearby, never a catch.
- **Hiding:** E at a wardrobe or a vestry cabinet puts you inside, with the door crack as your view. If he saw you (being heard is not enough) within 1.75 s of climbing in, he comes straight over and drags you out. Otherwise you are safe in there: he never opens an occupied wardrobe on a sweep. He only goes for a wardrobe he can walk to, gives up after 12 s, and drops it for anyone he sees in the open. He whispers "I see you" to the one he is coming for. Tearing the door open shows his face full screen, then he holds for `dragOutHoldSeconds` (1.6 s) before he can strike: a heartbeat to run. Wardrobes he rattles during a sweep are only ever empty ones.
- **Pressure:** he patrols towards the players. When nobody has been near him for 35 s and everyone is over 50 m away, he rises out of the water somewhere closer (at most once every 50 s). He forces his way along his path after 1.2 s without progress. He is held back for the first 20 s of a night and during every cutscene.
- **Voice:** taunts every 13 s or so while hunting, a line when he loses you, a line when he catches someone.

## Voice, cutscenes and scares

- **Voice lines:** 67 lines for Crane, Mara (in person and over her radio), Lily, the five singers and whispers, generated offline with Piper TTS using the `en_US-libritts_r-medium` voice (CC BY 4.0; see `CREDITS.md`) and processed for each speaker. The script is `Audio/Voice/Source~/lines.tsv` and the generator is `gen_voices.py`; **Ryan > Classic Horror > Rebuild Voice Bank** turns them into `Data/VoiceBank.asset` with each clip's length, which the server uses to queue lines so they never talk over each other. Subtitles are coloured per speaker and carry an emblem for where the voice comes from (the radio, Crane, a whisper, the dead, Mara in person); radio lines arrive with a burst of static. Crane's lines play from where he is.
- **Cutscenes:** `CH_CutsceneDirector` plays the shots authored in the map's `CH_CutsceneSet` (letterbox bars, a title card, fades, and two client-only puppets: Crane, and Mara in a long oilskin coat over dark overalls, rubber boots and gloves, a sou'wester over her hood and a face, all built from lathed and primitive meshes by `CH_Authoring.DressAsSkipper`; posed per shot: stand, walk, rise, lunge, sink, and the ferry itself, which a shot can sail to a mark with what is fitted aboard) on wall-clock time, so a slow frame cannot leave the camera behind the server while the server holds the night and the priest; the cue times in `CH_Story` match the clips, and a test checks that no line runs into the next. While a scene plays, `CH_CutsceneStage` switches off every networked character (players, their tools and lights, the real Crane) and fades out the shared player list and chat (their canvas groups, so nothing reopens or re-lays out afterwards), and afterwards puts back exactly what it switched off.
- **Jump scares:** the priest's face full screen (`Data/EncounterFace.png`, rendered by `CH_EncounterFace`; a build without a graphics device keeps the committed picture) for the fatal catch, the bell grab and being dragged out of a wardrobe, close-call warnings (at most three per player per night, 60 s apart, face to face within 7 m, holding Crane for 0.9 s), apparitions of Crane watching from somewhere you will look (every 70 to 140 s), and ambient scares for everyone (a door slamming 10 to 26 m away, every lamp dying for 3.5 s, a cry, the bell) every 55 to 110 s, never during a chase.
- **Fear:** heartbeat, breathing, drone, the red screen edge and stuttering lamps as he closes in; the chase music and a sting when a chase starts. These come from the shared game-feel kit.

## HUD

Built by **Rebuild HUD** (`CH_Authoring.RebuildHud`) from the shared `HudKit`: the act card at the top (act, objective, progress, the night's clock, the journal key and new entries), the objective compass under it, subtitles at the bottom, and the **Carrying** chip and the hiding label just above the hotbar, clear of everything else. The shared top-bar health bar is hidden for the whole night (`ClientHealthbar.Suppressed`, set by `CH_ClientController`): Crane takes lives, not health, so a bar that never moves would only mislead.

## Saving

`CH_SaveData` (version 2) keeps, per account: nights played, escapes, true endings, which endings you have seen, singers you have met, items delivered, chases escaped, times caught and your fastest escape. The record is sent to the player when they join and when a night ends, and the ending card shows it: "Endings 2/3 · Singers met 3/5 · Best escape 07:38 · Nights 6". Delivering an item gives 15 XP and 5 credits, escaping 120 XP and 60 credits, and the true ending 80 XP more.

## Authoring

Run these after changing their inputs. Each rebuilds only what it owns and keeps the rest of the map.

- **Build Story Set** (`Editor/CH_StorySetAuthoring.cs`) places the night's set pieces under `CH_Map/Story Set`: a jetty run out from the nearest open, tree-free stretch of the lake to `ShoreHint` (the south-west shore, outside the flood walls), its planks lifted above `CH_Map.waterLevel` so the deep-water NavMesh exclusion keeps them; the moored boat beside it with its part, bell and boarding marks on the hull's boards; the players' arrival point at the jetty's foot; the five singers' engraved gravestones and their slots, Lily's marker, the altar table in front of the pulpit, the hiding places (ground-floor cabinets with room in front and an exit on the NavMesh, plus two new vestry wardrobes), the music box's bedroom spots, every cutscene shot, and a low mist over the open lake (`BuildLakeMist`) so the water reads as water from the shore. Singers' graves are only ever slab headstones (the obelisk has no face wide enough for a name). It then wires the voice bank and prefabs into `CH_Map` and checks the map is complete.
- **Rebuild Presentation Prefabs** rebuilds the flashlight, the shared NPC characters, the item prefabs, the cutscene priest and Mara puppets, the voice bank and the HUD. **Rebuild Item Prefabs**, **Rebuild Priest Actor**, **Rebuild Voice Bank** and **Rebuild Investigation HUD** rebuild one part each.
- **Block Presence At Fences And Flood Walls** keeps the priest's agent from crossing fences and flood walls a player cannot, then rebakes `Scenes/classic_horror_start/NavMesh-Investigation.asset`. Run it after **Build Story Set** so the jetty is walkable for him (the boat is left out of the bake).
- **Configure Realtime Map Lighting** reapplies the night lighting, lantern posts, night grade and particle materials. **Diagnostics > Capture Lighting Probe** renders the view from the arrival point towards the boat and logs the lighting.

`fixedSeed` on the runner reproduces a night (0 picks a fresh seed each night).

## Tests

EditMode tests in `Tests/`: `CH_NightTests` (seeding, act gating, items that swap, are put down and drop but never vanish, the lake is never a shortcut, the drowning rule, boat slots reached above the gunwale, parts and relics only fitting their own place, closed graves, the secret music box, every ending, nothing accepted once the night is over), `CH_BalanceTests` (the model uses the shared character's real sprint cost, a sprint-and-walk runner reaches cover 120 m away in every act and a walker is caught, after 300 m in the open he is still within sight in every act, and still is when corners cost him a fifth of his pace (but would not be without closing in), closing in never outruns a sprinter, from Act II a team that never breaks his sight is run down, the bell run from the altar is winnable alone and as a team but not without his stare, a chased fuel carrier has to put the can down, a drag-out leaves a chance to run), `CH_StoryTests` (every line the story names has a clip, item names read correctly mid-sentence, cutscene lines fit their scene without overlapping, shot lookup, scare rationing, the priest's tuning: he outruns a walker but never a sprinter, gets faster each act and hardest after the bell carrier, hears the bell farthest, cannot hear someone standing still, and sees a lit flashlight farther) and `CH_MaterialTests` (the generated glow materials keep their emission).
