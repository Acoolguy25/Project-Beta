# The Drowned Choir

A cooperative first-person horror night for one to four players in Flooded Grounds' drowned village of Hollowmere. You and Mara came out by ferry to look for her missing brother; the ferry is wrecked, and Father Silas Crane, the priest who drowned his own congregation in the 1962 flood, is still collecting voices for his choir. The server owns the night: its acts, every item, the priest's hunt, the scares, the voice lines and the endings.

## Play

Launch the client from `Assets/Scenes/MainMenu.unity` and join **The Drowned Choir** in the game browser. Server builds use `-universe_id=classic_horror`. In the Editor, pick `classic_horror` in **Ryan > Server > Editor Universe**, play `Assets/Scenes/ServerInit.unity` in the server Editor (the ParrelSync clone) and MainMenu in the client Editor.

| Input | Action |
| --- | --- |
| WASD / mouse | Move / look (first person) |
| Shift | Sprint (shared stamina) |
| Left click | Flashlight on or off |
| E | Pick up, set down or place the item under the reticle; climb into or out of a wardrobe |
| F | Journal: everything said tonight, newest first |

## The night

1. **Prologue (cutscene, 37 s).** The wreck, Mara, the chapel bell, the first sight of Crane.
2. **Act I, Stranded.** Find the **fuel can**, the **ferryman's toolbox** and the **ferry lantern**, carry them one at a time to the boat moored at the jetty on the village's south-west shore (you start there, with the wreck out on the lake) and fit each in its place. Items spawn at least 30 m from the jetty.
3. **The Taking (cutscene, 29 s).** The boat is fixed; Crane drags Mara into the water. She keeps talking over her radio from the chapel.
4. **Act II, The Choir.** Three of the five drowned singers (Agnes Pell, Thomas Hale, Ruth Ellery, Elias Grey, Margaret Lowe; chosen by the night's seed) have left a relic out: a hymnal, a photograph or a vigil candle, each named for its owner. Lay each on its owner's grave; the other two graves stay closed. Each freed singer says goodbye.
5. **The Chapel Opens (cutscene, 10 s).** The doors open and Crane's bell is on the altar.
6. **Act III, The Last Sermon.** Take the bell and carry it to the boat. It rings while you carry it: Crane hears it across the map (80 m), chases its carrier faster than anyone else, and the carrier cannot hide.
7. **Boarding (25 s).** Everyone gets to the jetty before the boat leaves. Then the **Departure** cutscene and the ending.

**Lily's music box** is the secret. It sits in one of five bedrooms with no objective arrow. Lay it on Lily's little marker by the jetty to earn the true ending.

**Endings:** *She came home* (escaped with the music box laid), *You escaped* (escaped without it), *The choir sings on* (everyone caught, or the night ran out after 40 minutes). The next night starts 20 seconds after the ending card with a new seed.

**Items never disappear.** You carry one at a time, in your hands, and everyone sees it. E on another item swaps the two, leaving yours where the new one was. Dying drops what you carried where you fell. A placed item stays placed. The fuel can is too heavy to run with (3.9 m/s); everything else can be carried at a sprint.

**Lives:** three per player per night, tracked per account so rejoining does not refill them. Deep water pulls you out without costing a life (`drowningCostsLife`, off).

## Father Crane

All of his numbers live in `CH_MonsterTuning`, serialized on the runner (`Server/CH_ServerRunner.prefab`); `CH_Monster` reads everything from there and the tests check the same fields.

- **Senses:** a 130° sight cone, 55 m on a lit player and 22 m on one with the flashlight off, 65 m once he is chasing. He hears sprinting through walls at 30 m and walking at 11 m; standing still is silent. Within 3.5 m he senses you whatever you do.
- **Chase:** 6.9, 7.4 and 8.0 m/s in acts I to III (players walk at 4.5 and sprint at 9.5), 8.6 m/s after the bell, 8% slower when only one player is alive. He aims 0.45 s ahead of where you are running, repaths every 0.2 s, and lunges at 11.5 m/s for half a second inside 5.5 m with a clear line.
- **Losing you:** he keeps going where you were heading for 5 s, then sweeps a 14 m area for 14 s, opening some of the wardrobes he passes (25%).
- **Hiding:** E at a wardrobe or a vestry cabinet puts you inside, with the door crack as your view. If he saw or heard you within 1.75 s of climbing in, he comes straight over and drags you out. Otherwise you are safe unless he picks your wardrobe during a sweep. He only goes for a wardrobe he can walk to, gives up after 12 s, and drops it for anyone he sees in the open. Being dragged out puts you in front of him, free to run if his blow does not land.
- **Pressure:** he patrols towards the players. When nobody has been near him for 35 s and everyone is over 50 m away, he rises out of the water somewhere closer (at most once every 50 s). He forces his way along his path after 1.2 s without progress. He is held back for the first 20 s of a night and during every cutscene.
- **Voice:** taunts every 13 s or so while hunting, a line when he loses you, a line when he catches someone.

## Voice, cutscenes and scares

- **Voice lines:** 67 lines for Crane, Mara (in person and over her radio), Lily, the five singers and whispers, generated offline with Piper TTS using the `en_US-libritts_r-medium` voice (CC BY 4.0; see `CREDITS.md`) and processed for each speaker. The script is `Audio/Voice/Source~/lines.tsv` and the generator is `gen_voices.py`; **Ryan > Classic Horror > Rebuild Voice Bank** turns them into `Data/VoiceBank.asset` with each clip's length, which the server uses to queue lines so they never talk over each other. Subtitles are coloured per speaker, and radio lines arrive with a burst of static. Crane's lines play from where he is.
- **Cutscenes:** `CH_CutsceneDirector` plays the shots authored in the map's `CH_CutsceneSet` (letterbox bars, a title card, fades, a stand-in priest actor) while the server holds the night and the priest; the cue times in `CH_Story` match the clips, and a test checks that no line runs into the next.
- **Jump scares:** the fatal catch, close-call warnings (at most three per player per night, 60 s apart, face to face within 7 m, holding Crane for 0.9 s), apparitions of Crane watching from somewhere you will look (every 70 to 140 s), and ambient scares for everyone (a door slamming 10 to 26 m away, every lamp dying for 3.5 s, a cry, the bell) every 55 to 110 s, never during a chase.
- **Fear:** heartbeat, breathing, drone, the red screen edge and stuttering lamps as he closes in; the chase music and a sting when a chase starts. These come from the shared game-feel kit.

## Saving

`CH_SaveData` (version 2) keeps, per account: nights played, escapes, true endings, which endings you have seen, singers you have met, items delivered, chases escaped, times caught and your fastest escape. Delivering an item gives 15 XP and 5 credits, escaping 120 XP and 60 credits, and the true ending 80 XP more.

## Authoring

Run these after changing their inputs. Each rebuilds only what it owns and keeps the rest of the map.

- **Build Story Set** (`Editor/CH_StorySetAuthoring.cs`) places the night's set pieces under `CH_Map/Story Set`: a jetty run out from the nearest open, tree-free stretch of the lake to `ShoreHint` (the south-west shore, outside the flood walls), its planks lifted above `CH_Map.waterLevel` so the deep-water NavMesh exclusion keeps them; the moored boat beside it with its part, bell and boarding marks on the hull's boards; the players' arrival point at the jetty's foot; the five singers' engraved gravestones and their slots, Lily's marker, the altar table in front of the pulpit, the hiding places (ground-floor cabinets with room in front and an exit on the NavMesh, plus two new vestry wardrobes), the music box's bedroom spots, and every cutscene shot. It then wires the voice bank and prefabs into `CH_Map` and checks the map is complete.
- **Rebuild Presentation Prefabs** rebuilds the flashlight, the shared NPC characters, the item prefabs, the cutscene priest actor, the voice bank and the HUD. **Rebuild Item Prefabs**, **Rebuild Priest Actor**, **Rebuild Voice Bank** and **Rebuild Investigation HUD** rebuild one part each.
- **Block Presence At Fences And Flood Walls** keeps the priest's agent from crossing fences and flood walls a player cannot, then rebakes `Scenes/classic_horror_start/NavMesh-Investigation.asset`. Run it after **Build Story Set** so the jetty is walkable for him (the boat is left out of the bake).
- **Configure Realtime Map Lighting** reapplies the night lighting, lantern posts, night grade and particle materials. **Diagnostics > Capture Lighting Probe** renders the view from the arrival point towards the boat and logs the lighting.

`fixedSeed` on the runner reproduces a night (0 picks a fresh seed each night).

## Tests

EditMode tests in `Tests/`: `CH_NightTests` (seeding, act gating, items that swap and drop but never vanish, parts and relics only fitting their own place, closed graves, the secret music box, every ending, nothing accepted once the night is over), `CH_StoryTests` (every line the story names has a clip, cutscene lines fit their scene without overlapping, shot lookup, scare rationing, the priest's tuning: he outruns a walker but never a sprinter, gets faster each act and hardest after the bell carrier, hears the bell farthest, cannot hear someone standing still, and sees a lit flashlight farther) and `CH_MaterialTests` (the generated glow materials keep their emission).
