# Game feel kit (`RyanAssets.GameFeel`)

Shared "juice" for every universe: sounds, camera shake, floating text, hit flashes, punches,
screen effects, particles, post-processing moods and character feel. Client only: the assembly has
the `!UNITY_SERVER` define constraint, so it compiles out of dedicated servers and the server
Editor. Call it from client code (your universe's `Client` asmdef) only.

## Setup

1. Add `RyanAssets.GameFeel` to your client asmdef's references (by name).
2. `using RyanAssets.GameFeel;`
3. That's it. Everything is static and needs no scene object: the kit creates one hidden
   `GameFeel` object on first use and loads its assets from `Resources/GameFeelSettings`.
   Calls outside Play mode do nothing.

Assets are generated: **Ryan > Game Feel > Rebuild** (batchmode:
`-executeMethod RyanAssets.GameFeel.Editor.GameFeelAuthoring.Build`). Never hand-edit the files in
`Generated/`; tune numbers in `Resources/GameFeelSettings.asset` (kept across rebuilds) or change
the authoring scripts in `Editor/`.

## Sounds: `GameSfx`, `SfxCue`, `SfxSound`, `SfxLadder`

Pooled one-shots through the SFX mixer group (the player's SFX Volume setting applies), with a
random variant and a little pitch/volume jitter per play, and rate limiting so 20 coins in one
frame play once.

| Call | Example |
|---|---|
| `GameSfx.Play(SfxCue cue, float volume = 1, float pitch = 1)` (2D) | `GameSfx.Play(SfxCue.Purchase);` |
| `GameSfx.PlayAt(SfxCue cue, Vector3 position, ...)` (3D) | `GameSfx.PlayAt(SfxCue.Explosion, pos);` |
| `GameSfx.Play(SfxSound sound, ...)` / `PlayAt(SfxSound, pos, ...)` | `GameSfx.PlayAt(mySounds.cannon, muzzle.position);` |
| `GameSfx.PlayClip(AudioClip, ...)` / `PlayClipAt(AudioClip, pos, ...)` | `GameSfx.PlayClip(voiceLine);` |
| `GameSfx.Get(SfxCue)` | `AudioClip[] clips = GameSfx.Get(SfxCue.Win).clips;` |
| `GameSfx.Semitones(steps)` | `GameSfx.Play(SfxCue.Coin, 1f, GameSfx.Semitones(3));` |
| `new SfxLadder(SfxCue cue, float semitoneStep = 1, int maxSteps = 12, float resetSeconds = 1.2f)` | `readonly SfxLadder coins = new(SfxCue.Coin);` then `coins.PlayAt(pos);` (each play inside 1.2 s is a semitone higher) |

Cues: `UiClick, UiHover, Coin, Pickup, Purchase, Build, Hit, Kill, Explosion, ExplosionLarge,
Whoosh, CountdownBeep, Go, Win, Lose, LevelUp, Error, Alarm, Footstep, Jump, Land, Horn` (Kenney CC0, plus a
synthesized CC0 horn,
see `Audio/CREDITS.md`). For a game's own sounds, serialize an `SfxSound` (clips, volume, jitter,
min interval, 3D distances) on your component and play it with the same calls.

## Camera shake: `CameraShake`

Works with every camera mode (first/third person, top-down, commander): the offset is applied only
while the main camera renders, so aiming and raycasts never see it. Strength is 0..1.

| Call | Example |
|---|---|
| `CameraShake.Kick(float strength = 0.4f, float seconds = 0.25f)` | `CameraShake.Kick(0.25f, 0.15f);` (recoil) |
| `CameraShake.Explosion(Vector3 pos, float radius, float strength = 1, float seconds = 0.6f)` | `CameraShake.Explosion(blast, 30f);` |
| `CameraShake.Stop()` | before a cutscene |
| `CameraShake.Intensity` (0..1, get/set) | the player's "Camera Shake" slider (Game menu > Camera); 0 = off |

`Explosion` measures distance to the local character, else to the camera, else (overhead cameras)
to the camera's line of sight.

## Camera FOV: `CameraFov`

Extra field-of-view degrees added only while the main camera renders (like the shake), so it stacks
on any rig: a Cinemachine brain that is not on `Camera.main`, a game writing `Camera.fieldOfView`, or a
fixed camera. Orthographic cameras are left alone. Caps and easing: GameFeelSettings > Camera FOV.

| Call | Example |
|---|---|
| `CameraFov.Kick(float extraDegrees, float seconds = 0.35f)` | `CameraFov.Kick(6f);` (launch pad, dash) |
| `CameraFov.SetBoost(float amount01)` | `CameraFov.SetBoost(sprinting ? 1f : 0f);` (eased; 1 = `boostDegrees`, 8 by default) |
| `CameraFov.SetBoostDegrees(float extraDegrees)` | `CameraFov.SetBoostDegrees(speed01 * 12f);` |
| `CameraFov.CurrentExtraDegrees` | what is being added this frame |
| `CameraFov.Clear()` | before a cutscene |

`Camera.main.fieldOfView` keeps reading the rig's own value; a game that writes it directly keeps
working, with the kit's degrees added on top.

## Floating text: `FloatingText`

Pooled world-space TextMeshPro popups that pop, rise and fade, face the camera, draw over the world
and stay readable at any distance. Styles live in GameFeelSettings > Floating Text.

| Call | Example |
|---|---|
| `FloatingText.Damage(Vector3 pos, float amount, float size = 1)` | `FloatingText.Damage(hit.point, 35);` |
| `FloatingText.Money(Vector3 pos, double amount, float size = 1)` | `FloatingText.Money(pad.position, 120);` shows "+$120"; `12500` shows "+$12.5K" (the shared `RoundFormat.Money` style; under 10K it does not allocate; negative shows "-$") |
| `FloatingText.Money(Vector3 pos, double amount, Func<double, string> format, float size = 1)` | `FloatingText.Money(pos, gems, g => g.ToString("N0"));` your own number text, "+$"/"-$" prefixed |
| `FloatingText.Heal(pos, amount, size = 1)` | `FloatingText.Heal(pos, 25);` |
| `FloatingText.Callout(pos, string text, [Color color], size = 1)` | `FloatingText.Callout(flag, "CHECKPOINT");` |
| `FloatingText.Callout(pos, string text, Color color, float size, float seconds)` | `FloatingText.Callout(boss, "THE WARDEN", Color.red, 1.2f, 3f);` (stays up longer) |
| `FloatingText.Spawn(pos, string text, Color color, float size = 1)` | `FloatingText.Spawn(pos, "COMBO x3", Color.cyan);` |
| `FloatingText.Spawn(pos, string text, in FloatingTextStyle style)` | full control (colour, size, rise, duration, pop, scatter) |
| `FloatingText.Number(pos, string format, float value, Color color, float size = 1)` | `FloatingText.Number(pos, "+{0} XP", xp, Color.green);` (no allocation; TMP format, `{0:1}` = 1 decimal) |
| `FloatingTextStyle.WithDuration(float seconds)` | `FloatingText.Spawn(pos, "SAVED", style.WithDuration(2.5f));` |

Popups grow with distance to stay readable, but never cover more than
`GameFeelSettings > Floating Text > maxScreenHeight` of the screen height (9% by default), so a callout
right next to the camera no longer fills the screen.

For your own world-space labels, `RyanAssets.Rounds.Billboard` now has `facing = Facing.CameraPlane`
(align with the camera plane: no skew near the screen edges; the default `LookAtCamera` is the old
behaviour) and `maxScreenHeight` (0..1 of the screen height, 0 = off) with `referenceHeight` (the label's
world height at its authored scale), which scales the label down at close range, never up.

## Hit flash and punch: `HitFlash`, `Punch`

| Call | Example |
|---|---|
| `HitFlash.On(Component target, Color? color = null, float seconds = -1)` | `HitFlash.On(enemy, Color.white);` (finds or adds the component on the root) |
| `hitFlash.Flash()` / `Flash(Color, seconds)` | put `HitFlash` on a prefab, list renderers or leave empty for all children |
| `Punch.Scale(Transform t, float amount = 0.15f, float seconds = 0.3f)` | `Punch.Scale(coinIcon.transform);` |
| `Punch.Squash(Transform t, float amount = 0.2f, float seconds = 0.35f)` | `Punch.Squash(model, 0.25f);` (local Y; negative stretches first) |
| `Punch.Pop(Transform t, float from = 0.6f, float seconds = 0.3f)` | `Punch.Pop(newTower.transform);` (spawn grow-in) |
| `Punch.Cancel(Transform t)` | restores the original scale |

HitFlash pushes `_BaseColor` (or `_Color`), plus any other listed colour property (`colorProperties`; the
robot's recolour shader paints over the base colour, so `_RobotPrimaryColor` is listed by default), towards the
flash colour times `brightness` (HDR, default 3, because the base colour multiplies the texture) through
MaterialPropertyBlocks, so no material copies. Punches work on
RectTransforms too and never compound (a second punch restarts from the original scale).

## Screen effects: `ScreenFx`

One overlay canvas, created on first use, sorted at -10: below every HUD canvas (the shared HUD and
the games' HUD canvases sort at 0 or above), so vignettes and flashes never tint the HUD. The centre
text has its own canvas above the HUD (60).

| Call | Example |
|---|---|
| `ScreenFx.Flash(Color color, float seconds = 0.25f)` | `ScreenFx.Flash(new Color(1,1,1,0.6f));` (alpha = peak) |
| `ScreenFx.DamagePulse(float strength = 1)` | on every hit the local player takes |
| `ScreenFx.SetLowHealth(float health01)` | call on health change; red heartbeat at 30% and below, 1 = off |
| `ScreenFx.SetLowHealth(float health01, float intensityCap)` | `ScreenFx.SetLowHealth(hp01, 0.5f);` (half-strength vignette, for already dark games) |
| `ScreenFx.SetDanger(float strength, Color color, float maxAlpha = 0.55f, float pulsesPerSecond = 0)` | `ScreenFx.SetDanger(seekerNear01, Color.red, 0.45f, 1.3f);` (heartbeat); 0 fades it out |
| `ScreenFx.SetDanger(float strength)` | keeps the last colour, alpha and pulse |
| `ScreenFx.DangerFlash(Color color, float seconds = 1.2f, float maxAlpha = 0.55f)` | `ScreenFx.DangerFlash(Color.red);` ("base critical": a moment, not a permanent wash) |
| `ScreenFx.ClearDanger()` | |
| `ScreenFx.SetSpeedLines(float intensity)` | `ScreenFx.SetSpeedLines(boosting ? 1f : 0f);` |
| `ScreenFx.Countdown(int from = 3, string goText = "GO!", Action onGo = null)` | `ScreenFx.Countdown(3, "GO!", StartRace);` (beeps + "go" voice) |
| `ScreenFx.ShowText(string text, Color color, float seconds = 1.6f)` | `ScreenFx.ShowText("ROUND 2", Color.yellow);` |
| `ScreenFx.ShowText(string text, Color color, float seconds, float size)` | `ScreenFx.ShowText("REBIRTH 1!", Color.white, 1.6f, 0.6f);` (size multiplies the largest font size) |
| `ScreenFx.CancelCountdown()` | |

The danger vignette is edge-only (a rounded-rectangle falloff; the centre 60% of each axis stays
clear); the colour's alpha is ignored, `maxAlpha` is the peak. Centre text never spans more than 60% of
the screen width (`centerMaxWidth`): longer text auto-sizes down to `centerMinFontSize`.

Colours and timings are serialized on `Generated/Prefabs/ScreenFxOverlay.prefab` (set them in
`Editor/GameFeelAuthoring.cs` so they survive a rebuild).

## Particles: `GameFx`, `FxKind`, `FxHandle`

Pooled, tintable (tint multiplies, white = as designed), Y-up effects; pass an up vector to tilt
them to a surface or a custom gravity.

| Call | Example |
|---|---|
| `GameFx.Play(FxKind kind, Vector3 pos, float scale = 1)` | `GameFx.Play(FxKind.Confetti, finish.position);` |
| `GameFx.Play(kind, pos, Color tint, float scale = 1)` | `GameFx.Play(FxKind.DustPuff, feet, Color.gray);` |
| `GameFx.Play(kind, pos, Vector3 up, Color tint, float scale = 1)` | `GameFx.Play(FxKind.Sparks, hit.point, hit.normal, Color.white);` |
| `GameFx.Play(kind, pos, Quaternion rotation, Color tint, float scale = 1)` | |
| `FxHandle GameFx.Start(kind, pos, Color tint, float scale = 1)` (looping) | `beacon = GameFx.Start(FxKind.BeaconBeam, zone, teamColor);` ... `beacon.Stop();` |
| `FxHandle GameFx.Attach(kind, Transform target, Vector3 localOffset, Color tint, float scale = 1)` | `trail = GameFx.Attach(FxKind.Trail, rocket, Vector3.zero, Color.yellow);` (stops itself if the target is destroyed) |
| `GameFx.Explosion(Vector3 pos, bool large = false, float radius = 30)` | effect + sound + camera shake in one call |
| `GameFx.Poof(Vector3 feet, float height, Color tint, float scale = 1)` (also `Poof(feet, height, scale)` and `Poof(feet, Vector3 up, height, tint, scale)`) | `GameFx.Poof(robot.position, 5.6f, Color.white);` a full-body poof: up to 8 dust puffs layered from feet to head, each at most 1.6x, instead of one stretched puff |
| `handle.Stop(bool clear = false)`, `handle.IsPlaying`, `handle.SetPosition(pos)`, `handle.SetTint(color)` | a default/stale handle is safe to use |
| `GameFx.ExplosionEffectFeedback` (bool, default true) | shared `ExplosionEffect` blasts get camera shake, plus `SfxCue.Explosion`/`ExplosionLarge` when the prefab authors no sound (GameFeelSettings > Shared Explosions). Set false for a game with its own blast feedback. |

Kinds: `DustPuff, Sparks, Confetti, CoinBurst, ExplosionSmall, ExplosionLarge, Smoke, Heal,
ShockwaveRing, BeaconBeam (looping), Trail (looping)`. Sizes at scale 1 suit a 1-3 m object; the
robot is about 5.6 m tall (scale 3), so scale effects on characters up (the character feel uses
`0.22 * character scale` for dust), or use `GameFx.Poof` for whole-body smoke. Dust puffs emit 0.3 m
(times scale) above their position so the camera-facing quads do not cut into the floor.

## Moods: `MoodVolume`, `MoodPreset`

URP Volume profiles `ArcadeBright`, `WarmIndoor`, `Night`, `Horror` (`Generated/Moods`).

| Call | Example |
|---|---|
| `MoodVolume` component (needs a `Volume`; previews in the Editor) | add to an object in your client scene/prefab, pick the preset |
| `MoodVolume.Apply(MoodPreset preset, float seconds = 1)` | `MoodVolume.Apply(MoodPreset.Night, 3f);` (crossfades a kit-owned global volume, priority 20) |
| `MoodVolume.Clear(float seconds = 1)` / `MoodVolume.Active` | back to the scene's own look |

Volumes only show on a camera with Post Processing on. `MoodVolume.Apply` turns it on for the main
camera while a mood is showing and back off after `Clear` (GameFeelSettings > Moods >
Enable Post Processing); a `MoodVolume` component in your own scene needs a camera that has it on.

## Character feel (automatic)

Every character the client sees gets, while within 70 m of the camera: a jump sound and dust puff
on take-off, a squash (on the model, not the root) and dust on landing scaled by impact speed, and
dust from the feet while running. It follows the character's own up, so custom gravity
(`CharacterGravity`) works, and reads the synced Animator `Grounded` parameter, so remote players get
it too. Footstep and landing sounds already come from `CharacterAnimator` (the robot's Kyle clips);
`CharacterAnimator.Footstep` / `Landed` events expose those animation events. Tune or turn it off in
GameFeelSettings > Characters. (Obby's `OB_Fx` jump/land puffs now double up with this: remove them.)

A hard landing of the local player also kicks the camera (from 18 m/s, scaled up to the hard-landing
speed, and by the player's Camera Shake setting). It never fires within 0.75 s of a teleport: jumps of
over 25 m in a frame are detected, and code that warps a character can say so.

| Call | Example |
|---|---|
| `CharacterFeel.LandingShake` (bool, default true) | `CharacterFeel.LandingShake = false;` for a game with its own camera work |
| `CharacterFeel.NotifyTeleported(GameCharacter character)` | after a checkpoint warp or respawn: drops the measured fall, no shake on arrival |

## Shared UI feel (com.ryan.ui)

- Every button built by `UIKit.Clickable` (all shared and HudKit HUDs) gets `UIButtonFeel`: hover grow,
  press shrink, hover/click sounds from `UITheme` (Sound section). Game HUDs pick it up when their
  authoring menu is re-run. For a hand-made button, add `UIButtonFeel`.
- `UISound.Click()` / `UISound.Hover()` / `UISound.Play(clip, volume)` play UI sounds from code.
- `HudPanel` panels and the prompt dialog pop in (`UITheme.panelPopScale`, `CanvasGroupController.popFrom/popTarget`).
- Name tags shrink so they never cover more than 7% of the screen height and fade out between 60 and
  95 m (`NameTagManager` prefab fields). A game can change that while it runs:
  `var view = NameTagManager.OverrideView(float fadeStartDistance, float fadeEndDistance, float maxScreenHeight = 0)`
  (0 keeps the prefab's screen cap; the fields can be changed later, e.g. with commander zoom), and
  `view.Dispose()` restores the previous values. The newest override wins.
- `NameTagManager.EntityTagFilter` (`Func<EntityBase, bool>`, null = show all) hides shared entity tags
  (vehicles, structures, objectives) like `CharacterTagFilter` does for characters.
- Music (`MusicService`, on the Client prefab): a track change with no time to fade out and in (a track
  list whose `minDelay` is 0, e.g. the horror chase music) now crossfades the two tracks over
  `switchCrossfade` (0.6 s, serialized; 0 = the old hard cut). Lists with a delay keep their fade-out,
  silence, fade-in.

## Other shared client hooks (outside the kit)

| Hook | Where | Raised |
|---|---|---|
| `static event Action<LaunchPad, CharacterMovement> LaunchPad.Launched` | `RyanAssets.Platformer` (client only) | after a pad launches the local character |
| `static event Action<Vector3, Vector3, Vector3, bool> WeaponFireEffects.ShotShown` (muzzle, impact point, impact normal, hasImpact) | `RyanAssets.Shared.Combat` | on clients, for every shot shown, local and remote |
| `static event Action<ToolBaseClient, Vector3> ToolBaseClient.Activated` (tool script, aimed point) and `ToolBaseClient.SharedTool` | `RyanAssets.Tools.Client` | when a tool actually fires/swings/toggles, where it is controlled (owner's client, or the server for NPC tools) |
| `static event Action<ExplosionEffect, Vector3, float> ExplosionEffect.Exploded` (effect, centre, drawn scale), plus `DamageRadius`, `HasAuthoredAudio` | `RyanAssets.Shared.Combat` | on clients when a shared blast is drawn (the kit's explosion feedback uses it) |

Server side, tool hits are now dropped while a server lockout (`ToolBaseShared.UpdateServerCooldown`,
e.g. a jammed pistol or a kill cooldown) is running, less `ToolBaseShared.serverCooldownTolerance`
(0.15 s, serialized) for latency; `ToolBaseShared.ServerCooldownBlocksHits` reports it.

## Shared rendering settings (URP)

Every game and the server use `Assets/UnityTechnologies/Settings/StarterAssetsURPAsset.asset` (the
Graphics default; the quality levels point at no asset of their own and the build profiles do not
override it), with `StarterAssetsURPRenderer.asset` (Forward). It has:

- Screen Space Ambient Occlusion: Depth source, half resolution, 8 samples (Medium), Gaussian blur,
  After Opaque (applied to all opaque shaders, custom ones included; never to transparents, UI or
  world-space text), intensity 1.2, radius 0.2, falloff 60 m. Larger radii halo around near objects in
  first person (hide_and_seek), so keep it small.
- Soft shadows (medium), 2 cascades (split 25%), 80 m shadow distance, 2048 shadow atlas.
- Forward rendering with 8 additional lights per object (was 4). Forward+ was considered but not used:
  the scenes need at most a handful of lights per object, Forward is cheaper on low-end GPUs, and
  switching changes every shader's variants and light lists for all games at once.

## Gotchas

- Client code only. Shared or server code must not reference `RyanAssets.GameFeel` (it does not
  exist under `UNITY_SERVER`); trigger feedback from client callbacks (SyncVar changes, RPCs, broadcasts).
- Strings: `FloatingText.Spawn(pos, $"+{x}")` allocates; prefer `FloatingText.Number` in hot paths.
- Looping kinds played with `GameFx.Play` stop after 2 s (`GameFxLibrary.loopingPlaySeconds`); use
  `Start`/`Attach` and keep the handle.
- The kit's object lives across scene loads; stop your handles (`beacon.Stop()`) when your game ends.
- Effect bodies are alpha blended (additive only for accent glows), so they read on bright scenes like
  obby's white floors as well as at night. Flat rings sit 0.25 m above their position so they do not
  z-fight with the floor they are played on.
