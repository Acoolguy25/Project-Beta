# Murder Mystery

Server-authoritative murder mystery on the old mansion map. Mode is picked by a vote at the start of each
round (`MM_VoteMode`): NPCs vs Players, Classic, or Infection.

## Classic (MM2-style)

One murderer with a knife, one sheriff with a gun, everyone else innocent. Nobody reports bodies and there
are no meetings: the round is a chase. The murderer wins by eliminating everyone; the innocents win if the
murderer is shot or the clock runs out with anyone left.

| Phase | Length | What happens |
|---|---|---|
| Role reveal | 6 s | Roles are dealt (`MM_Roles.DealClassic`), everyone is invulnerable, each player sees a private role card with their goal. |
| Hunt | 180 s | The murderer stabs up close or throws the knife (**E**). The sheriff has one bullet at a time (2.2 s reload). Coins spawn round the house for everyone to collect. The top bar shows how many innocents are left and the clock. |
| Results | 12 s | "Innocents win" or "Murderer wins", who stopped the murderer, the cast (murderer, sheriff, hero), your own round (win or loss, coins, XP), progress to the next knife skin, every character's role, fate, kills and coins, and your odds for the next round. |

The round fills to 18 characters with NPCs (at least 4). One murderer and one sheriff per 24 characters.

### The roles

- **Murderer**: 8 s grace at the start of the hunt, then may kill at will (1.5 s between player kills,
  9 s for NPC murderers). Stab with the knife, or press **E** with the knife drawn to throw it at the
  cursor: the server flies it (75 m/s, 90 m range, `MM_KnifeThrows`), so it can be dodged and it sticks in
  walls. 3 s between throws.
- **Sheriff**: shoot the murderer. Shoot an innocent and the sheriff dies too, dropping the gun
  (`wrongShotPenalty`, MM2 rules; `Jam` instead jams the gun for 20 s).
- **Innocent**: survive and collect coins. When the sheriff dies, their gun drops with a beam over it; the
  first living, empty-handed innocent **player** to touch it becomes the **Hero** (a sheriff in gold). Only
  the hero is told who it is; everyone else hears "Someone took the gun".

NPC sheriffs are not told who the murderer is (`MM_NpcSheriff`): they wander with the gun holstered until
they see a murderer with the knife out or see (or hear, within 14 m) a kill, then react after 0.9 to 1.6 s
and fire with an aim that drifts with distance. A wrong shot kills them too. NPC townsfolk only run from a
murderer who gives themselves away (knife out, a recent kill or throw: `LocalNPC.FleeThreatFilter`), so a
murderer who keeps the knife away blends in. NPC murderers sometimes throw from mid range after a short
wind-up (the sheriff's chance to react).

If no player is left alive the round is cut to 25 s and the NPC murderers stop waiting between kills
("No players left"), so spectators are not kept watching a long NPC chase.

### Who gets the big roles

`MM_RoleOdds` (unit-tested): players weigh 3 in the draw against 1 for an NPC, and every round a player
misses a role adds 1.5 to their weight for it (up to 12 rounds), saved between sessions. The murderer is
drawn first, the sheriff from whoever is left. Players see their exact chance for the next round in the
lobby and on the results card.

### Coins and knife skins

Coins appear round the house (30 at the start, then one every 0.8 s up to 45 on the map). Each player's bag
holds 40 a round; the coin chip on the role chip counts it and a full bag says so. Coins are saved as a
lifetime total that unlocks knife skins (`MM_KnifeSkins`): Rusty, Crimson (60), Jade (180), Gilded (400),
Frost (750), Nightmare (1200), Inferno (2000). A murderer's knife, in the hand and in flight, wears their
best skin for everyone to see. XP: 1 per coin, 10 per kill, 40 for stopping the murderer, 15 for surviving,
25 for winning.

All of these numbers live in one serializable block, **Classic Tuning** (`MM_ClassicTuning`) on the
`MM_ServerRunner` component, so they can be tuned in the Inspector without code changes. What ships today
is the code defaults in `MM_ClassicTuning`: `MM_ServerRunner.prefab` (folder root) has no `classicTuning`
values in it. The prefab only saves from an Editor in the Linux Server build profile; once it is saved
there, the values in the prefab win over the code defaults.

### NPC murderers and the sheriff

`MM_NPC` lets NPC murderers prefer an armed sheriff over other targets: within `sheriffNoticeDistance`
(30 m) at first, from anywhere after `sheriffManhuntAfterSeconds` (70 s), each further murderer joining
`manhuntStaggerSeconds` (35 s) later. Inside `sheriffCreepDistance` (22 m) they creep (6 m/s) instead of
charging, which is the sheriff's chance to see them coming.

Editor-only switch **Debug First Player Role** on `MM_ServerRunner`: deals the chosen role to the first
player (for example Innocent, so an NPC gets the gun and one person can test the drop and the hero). It
does nothing in builds.

## Presentation and feel (client)

`MM_ClientHUD` reacts to the server's broadcasts with the shared game-feel kit (`RyanAssets.GameFeel`), and
`MM_KnifeFx` flies the thrown knives and paints the knife skins:

| Moment | Feedback |
|---|---|
| Role reveal | Pizzicato sting and a flash in the role's colour; again (at the top of the screen) when you become the hero. |
| Knife throw | A whoosh, the knife spinning along its line with a trail in the skin's colour; a thud and dust where it sticks in a wall. |
| Kill | Sparks, a shockwave ring under the victim and a kill sting where it happened, camera shake by distance (1.5x for the killer). Only players within earshot (`killAudibleDistance`, 45 m) are told. The victim gets a red flash and "YOU WERE KILLED". |
| Coin | A coin sound that climbs in pitch while you pick them up in a row, a floating +1, the coin chip pops; "BAG FULL" when it is. |
| Sheriff down | A horn and alarm, a blue flash, "The sheriff is down" with what it means for you, a beam and "SHERIFF'S GUN" over the gun. |
| Gun taken, last survivor, wrong shot | A banner at the top for everyone ("It's you" for the last survivor). |
| Final 10 s | A beep every second, higher for the last three. |
| Murderer cooldowns | The dagger chip counts the kill cooldown down, the throw prompt counts the throw down; a beep when the knife is ready. |
| Results | A win or lose sting, confetti over you when you win, "NEW KNIFE SKIN" when this round's coins unlocked one. |

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
  `Materials/MM_LampGlow.mat`, a point light in the shade at intensity 50 and range 24 m, a thin capsule
  collider and a NavMeshObstacle) goes against the nearest wall at each ceiling light and room fill,
  skipping any spot within 15 m of a lamp on the same storey. A "wall" is at least 4 m wide, so a pillar
  does not count, and a spot with no wall in reach (the middle of the Lower Hall, the stair at its north
  end) gets no lamp rather than one in the walkway. A ceiling light with no floor under it uses its
  nearest fill's floor instead. `ExtraLampAnchors` adds lamps where the map has no light: the landing at
  the top of the Secret Room stairs and two in the 30 m ramp corridor south of Workshop 1 (1 m from the
  side wall, without furniture, since the corridor is 5 m wide). Where the wall has room, the lamp comes
  as `Prefabs/MM_LampCorner.prefab`: an armchair, a cabinet and a rug round it (KayKit Furniture Bits,
  box colliders and NavMeshObstacles). The scene has 26 lamps, 7 of them corners. The obstacles do not
  carve (`m_Carve: 0`): NPCs steer round them, because a hole carved in the NavMesh at runtime cut the
  Control Room off in an earlier pass.
- The room fill nearest each lamp (within 10 m) is switched off: the lamp replaces it, so the floors do
  not stack more lights against the 8-per-object limit. The other fills are a short-range (13 m), nearly
  white bounce, so pools stay separate and characters keep their own colours; a fill within 3 m of a wall
  is dimmed in proportion (down to 25 %), so it does not paint a hard disc on the wall. The ceiling lights
  are a dim top light (24 m).
- The ambient is a cool blue strong enough that a wall no lamp reaches is dark blue rather than black. The
  map's walls and floors are a few meshes that span the whole house, and URP lights each object with only
  its 8 most important lights, so far from those a wall gets the ambient and nothing else.
- The Secret Room round the stair landing (no lights in the map) gets one extra room fill
  (`ExtraFills`), marked Important so the house-wide wall mesh picks it.
- `MM_Backdrop` (scene root, rebuilt by the tool): a panel in the map's Cement2 look
  (`Materials/MM_Backdrop.mat`) on the step down at the north end of the Lower Hall. The map has that face
  only from the other side, so the lower floor and the hall camera looked through it at the void.

The Upper Hall's floor and walls are inactive in the map (only the balcony edge is visible from the Lower
Hall), so it gets no lamps.

Pipeline limits (shared `StarterAssetsURPAsset`, not changed): Forward, 8 additional lights per object,
no additional-light shadows, so point lights are authored with shadows off. On the cloud test machine
(OpenGL Core) URP also keeps only 32 additional lights per camera (256 on DirectX/Vulkan/Metal), so
frames taken there can be darker than the game on a player's PC. The tool also puts the
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

`Server/MM_SaveData.cs` (version 2): rounds played, Classic wins per role, rounds survived, murders and
throw kills, murderers shot, guns picked up, lifetime coins (which unlock knife skins), and the luck that
raises a player's odds of being murderer or sheriff after rounds without the role.

## Code

- `Server/MM_ServerRunner.cs` spawning, roles, tools, win conditions, mode vote; sends each player an `MM_StateBroadcast` twice a second.
- `Server/MM_ClassicRound.cs` the Classic phases above: deaths, the gun drop and the hero, coins, knife throws, results, XP and saves.
- `Server/MM_KnifeThrows.cs` server-simulated thrown knives. `Server/MM_NpcSheriff.cs` the NPC sheriff. `Server/MM_Sight.cs` line-of-sight checks.
- `Shared/MM_Protocol.cs` broadcasts between server and client.
- `Shared/MM_ClassicTuning.cs`, `Shared/MM_KillClock.cs`, `Shared/MM_RoleOdds.cs`, `Shared/MM_RoundRules.cs`,
  `Shared/MM_DropRules.cs`, `Shared/MM_KnifeSkins.cs` Classic tuning and pure rules (tested in `Tests/`).
- `Client/MM_ClientHUD.cs` role card and chip, coin bag, throw prompt, odds, announcements, results, sounds and effects.
  `Client/MM_KnifeFx.cs` thrown knives and knife skins.
- `Editor/MM_Authoring.cs` (menu **Ryan > Murder Mystery > Rebuild HUD and Knife**) rebuilds `Prefabs/MM_HUD.prefab`
  and `Prefabs/MM_ThrownKnife.prefab` and makes sure the HUD is in `murder_mystery_start.unity`. Run it from a client Editor.
  The same menu has **Apply Atmosphere** for the scene (lighting, lamps, backdrop); it does not touch the HUD.
