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
Whoosh, CountdownBeep, Go, Win, Lose, LevelUp, Error, Alarm, Footstep, Jump, Land` (Kenney CC0,
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

## Floating text: `FloatingText`

Pooled world-space TextMeshPro popups that pop, rise and fade, face the camera, draw over the world
and stay readable at any distance. Styles live in GameFeelSettings > Floating Text.

| Call | Example |
|---|---|
| `FloatingText.Damage(Vector3 pos, float amount, float size = 1)` | `FloatingText.Damage(hit.point, 35);` |
| `FloatingText.Money(pos, amount, size = 1)` | `FloatingText.Money(pad.position, 120);` shows "+$120" |
| `FloatingText.Heal(pos, amount, size = 1)` | `FloatingText.Heal(pos, 25);` |
| `FloatingText.Callout(pos, string text, [Color color], size = 1)` | `FloatingText.Callout(flag, "CHECKPOINT");` |
| `FloatingText.Spawn(pos, string text, Color color, float size = 1)` | `FloatingText.Spawn(pos, "COMBO x3", Color.cyan);` |
| `FloatingText.Spawn(pos, string text, in FloatingTextStyle style)` | full control (colour, size, rise, duration, pop, scatter) |
| `FloatingText.Number(pos, string format, float value, Color color, float size = 1)` | `FloatingText.Number(pos, "+{0} XP", xp, Color.green);` (no allocation; TMP format, `{0:1}` = 1 decimal) |

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

One overlay canvas, created on first use, under the HUD (the centre text sits above it).

| Call | Example |
|---|---|
| `ScreenFx.Flash(Color color, float seconds = 0.25f)` | `ScreenFx.Flash(new Color(1,1,1,0.6f));` (alpha = peak) |
| `ScreenFx.DamagePulse(float strength = 1)` | on every hit the local player takes |
| `ScreenFx.SetLowHealth(float health01)` | call on health change; red heartbeat at 30% and below, 1 = off |
| `ScreenFx.SetSpeedLines(float intensity)` | `ScreenFx.SetSpeedLines(boosting ? 1f : 0f);` |
| `ScreenFx.Countdown(int from = 3, string goText = "GO!", Action onGo = null)` | `ScreenFx.Countdown(3, "GO!", StartRace);` (beeps + "go" voice) |
| `ScreenFx.ShowText(string text, Color color, float seconds = 1.6f)` | `ScreenFx.ShowText("ROUND 2", Color.yellow);` |
| `ScreenFx.CancelCountdown()` | |

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
| `handle.Stop(bool clear = false)`, `handle.IsPlaying`, `handle.SetPosition(pos)`, `handle.SetTint(color)` | a default/stale handle is safe to use |

Kinds: `DustPuff, Sparks, Confetti, CoinBurst, ExplosionSmall, ExplosionLarge, Smoke, Heal,
ShockwaveRing, BeaconBeam (looping), Trail (looping)`. Sizes at scale 1 suit a 1-3 m object; the
robot is about 5.6 m tall (scale 3), so scale effects on characters up (the character feel uses
`0.22 * character scale` for dust).

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

## Shared UI feel (com.ryan.ui)

- Every button built by `UIKit.Clickable` (all shared and HudKit HUDs) gets `UIButtonFeel`: hover grow,
  press shrink, hover/click sounds from `UITheme` (Sound section). Game HUDs pick it up when their
  authoring menu is re-run. For a hand-made button, add `UIButtonFeel`.
- `UISound.Click()` / `UISound.Hover()` / `UISound.Play(clip, volume)` play UI sounds from code.
- `HudPanel` panels and the prompt dialog pop in (`UITheme.panelPopScale`, `CanvasGroupController.popFrom/popTarget`).
- Name tags shrink so they never cover more than 7% of the screen height and fade out between 60 and
  95 m (`NameTagManager` prefab fields).

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
