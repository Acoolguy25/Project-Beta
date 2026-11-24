# Retention kit

The shared "one more match" loop for every Project Beta game:

- per-game **levels** with an unlock table
- **3 daily challenges** that reset at UTC midnight
- a **play streak**
- an animated **end-of-match summary**

The server owns all of the rules. The client only draws the state the server sends.

## Changes in the Sky Obby overhaul

Additions only; every game builds and behaves as before unless noted.

- **Level chip never steps back.** A game that grants XP during a match (Sky Obby pays per checkpoint) already showed part of it on the chip; when the summary arrives, the chip is held on the higher of the match's starting level and the level it already shows, and it follows the card's level-ups as the fill crosses them (`RetentionSummaryCard.LevelShown`, `RetentionLevelChip.ShownLevel` / `ShownFraction`). Games with no in-match XP see no change.
- **Unlock placeholder teaser.** The reserved unlock space shows a lock and "NEW UNLOCK COMING UP" until the reveal, instead of an empty outline. The shipped `Generated/RetentionHUD.prefab` was patched in place (the teaser objects were added; every existing fileID is kept, so the games' nested HUD overrides stay valid). A full rebuild builds the same teaser, but it currently gives every object a new fileID, so avoid it unless the games' HUD prefabs are rebuilt too.

## Changes in try 3

No public names changed. These are fixes plus a few additions (`RetentionSummaryCard.rewardIcon`, `infoIcon`, `unlockPlaceholder`, `FinalLevelShown`).

- **Nested HUD shows.** The prefab root is saved at scale 1 (it was 0: a root canvas's driven scale with no screen). `AddTo` sets the nested root's scale to 1, and `RetentionHud` resets a 0 scale on a nested root at Awake, so HUDs built before this fix also show. Rebuilding a game's HUD is not needed.
- **Toasts clear the player list.** The default toast spot is now below the shared HUD's top-right player list, with room for four players (200 px lower than before).
- **Detail lines.** The detail format no longer uses `</alpha>`, which TextMeshPro does not know and printed as text ("7 / 7</alp…"). A reward line with a detail and no gold lends the gold column to its label, so "4 wins in a row" fits.
- **Card.** Info lines use their own muted icon. The gold total counts up with the XP. The streak footer says what tomorrow brings, not the bonus the line already shows. The unlock's reserved space shows a faint outline until the reveal. The level chip stays on the match's starting level until the card reaches the new one (or is closed).
- **Leave-time flush.** Queued progress is applied when the client disconnects, which FishNet reports before it despawns the player and so before PlayerProgress releases the document.
- **GameFeel.** `ScreenFxOverlay.ClearDanger` now also clears a danger flash that is still playing.
- **One level-up line per match.** The kit pays account XP line by line, and `LevelsServer` put "has reached level N!" in global chat for every level crossed on every call. The kit now adds account XP itself and says only the final level, once, when the match finishes (or right away outside a match). `LevelsServer` is unchanged.
- **Lock icon.** The theme icon set has `lock` (Material, round), so games can draw locked cards with it (`HudKit.Icon("lock")`, or `lock` as an unlock icon name).

## Changes in try 2

Everything from try 1 still compiles unchanged; these are additions and fixes.

- **Layout.** Toasts moved to the top right, under the top bar (moved lower in try 3). The summary card moved to the left edge, centred. They no longer overlap, and the right-centre column stays free for the games' own feeds.
- **Summary card.** It no longer resizes while it animates: lines and the unlock block are laid out up front and faded in. Reaching the max level no longer runs the bar round an extra lap. The card can show info lines (`RewardLine.Info`, `WithDetail`).
- **Checked in Unity (phase B).** A long challenge name wraps instead of squeezing its rewards to "+..." in the panel. A long toast wraps onto more lines instead of running off the screen. The card is 480 wide, and an info line's detail may use the empty gold cell.
- **Clock skew.** A save dated exactly one day ahead of the server's clock counts as current (`RetentionRules.EffectiveDay`), so a few seconds of clock difference between servers never wipes a streak or pays anything twice. Only a save more than one day ahead is reset.
- **Same-day pick changes.** A done challenge is kept when the day's pick changes (a pool edit, or two builds live during a deploy), so it cannot pay again that day.
- **`Count` and `Reach`.**
  - A stat that no unfinished challenge of the day counts returns at once, with no allocation and no document change.
  - Progress is batched for each player and sent at most every `ProgressFlushInterval` (0.25 s). A completion is sent at once.
  - Progress goes out as the lean `RetentionProgressBroadcast` (day, index, progress).
- **Payouts.**
  - A challenge completion or streak claim saves the document, then the account, at once.
  - Unsaved players get no account XP or gold from daily rewards.
  - The fraction of account XP below 1 is carried to the next payout.
- **`Ready`.** The new `Ready(conn, level)` event and `IsReady(conn)` tell a game when a player's level is known. The streak greeting now also nudges new players and players whose streak lapsed.
- **Testable service.** The reward logic moved into `RetentionLedger` (Shared) behind `IRetentionSink`, and the service is now a thin FishNet and PlayerProgress adapter. EditMode tests cover payout-once and no-double-pay.
- **Input.** The challenges key is an `InputAction` (`challengesInput`, default `<Keyboard>/j`), so it can be rebound or given a gamepad binding.
- **Rollover.** The rollover loop waits 10 s after a failure instead of retrying in a loop.
- **API added:**
  - `RetentionService`: `Ready`, `IsReady`, `ProgressFlushInterval`.
  - `RetentionRules`: `EffectiveDay`, `UsesStat`, `CountsToday`, `Preview`, and the `StatEffect` enum.
  - `RewardLine`: the 5-argument constructor (with detail), `Info`, `WithDetail`, `Detail`, `HasReward`.
  - New types: `RetentionLedger`, `IRetentionSink`, `RetentionPlayer`.
- **Wire, append-only:**
  - `RetentionRewardView.detail`
  - `RetentionSnapshotBroadcast.challengeDay`
  - `RetentionChallengeBroadcast.day`
  - The new `RetentionProgressBroadcast`

  `RetentionChallengeBroadcast` is now sent only for completions.
- **Behaviour changes:**
  - `RewardLine.IsEmpty` is false for an info line.
  - `Refresh`, `ClaimStreak`, `Count`, `Reach` and `Snapshot` map the day through `EffectiveDay`.

| Folder | Assembly | Builds | What it holds |
| --- | --- | --- | --- |
| `Shared/` | `RyanAssets.Retention` | all | Rules (`RetentionRules`, `LevelTrack`, `DailyChallenges`, `StreakRules`), the `RetentionSave` block, `MatchSummary`, the broadcasts |
| `Server/` | `RyanAssets.Retention.Server` | `UNITY_SERVER` | `RetentionService<TSave>`: saves through `PlayerProgress.Modify` and pays account gold through `LevelsServer` and account XP like it (with one chat line per match) |
| `Client/` | `RyanAssets.Retention.Client` | `!UNITY_SERVER` | `RetentionHud` and its parts: level chip, challenges panel, toasts, summary card |
| `Editor/` | `RyanAssets.Retention.Editor` | Editor | `RetentionHudAuthoring`, which builds `Generated/RetentionHUD.prefab` |
| `../../Tests/Retention/` | `RyanAssets.Retention.Tests` | EditMode | Rule, save, pick and payout (ledger) tests |

**References each game assembly adds** (by name, in its asmdef):

| Game assembly | Add |
| --- | --- |
| Shared (rules, save block) | `RyanAssets.Retention` (and `FishNet.Runtime` if it does not have it yet) |
| Server (runner) | `RyanAssets.Retention`, `RyanAssets.Retention.Server` |
| Client (HUD controller) | `RyanAssets.Retention`, `RyanAssets.Retention.Client` |
| Editor (HUD authoring) | `RyanAssets.Retention.Client`, `RyanAssets.Retention.Editor` |
| Tests | `RyanAssets.Retention` |

## Integration (about 10 lines)

```csharp
// Shared: the game's rules (the same on server and client) and one block in the save document.
public static RetentionRules Rules(RetentionTuning t) => new("tower_defense", t,
    new[] { new UnlockDefinition(5, "frost_tower", "Frost Tower", "Slows enemies.", "castle") },
    new[] { new ChallengeDefinition("kills_150", "kills", 150, 120, 40, "Defeat {target} enemies"),
            new ChallengeDefinition("wave_10", "wave", 10, 150, 50, "Reach wave {target}") /* 6+ more */ });
public RetentionSave retention = new();                                    // field in TD_SaveData
public override void Validate() => retention = RetentionSave.Repair(retention);  // in TD_SaveData.Validate

// Server runner: Awake / OnPlayerAdded / gameplay / match end / OnDestroy.
retention = new RetentionService<TD_SaveData>(TD_Retention.Rules(tuning), save => save.retention);
retention.PlayerJoined(player);  retention.BeginMatch(conn);  retention.Count(conn, "kills");  retention.Reach(conn, "wave", wave);
retention.FinishMatch(conn, "Victory!", new RewardLine("Victory", 100), new RewardLine("Waves held", 5 * wave, 20), RewardLine.Info("Stars", "3 / 3"));
retention.Ready += (conn, level) => ApplyLoadout(conn);   // the player's level is known; IsUnlocked is reliable from here
retention.Dispose();                                     // OnDestroy: without it the snapshot-request and OnPlayerRemoved handlers outlive the game
```

**Client.** Put `Generated/RetentionHUD.prefab` in the game's HUD prefab. A game's own HUD authoring can call `RetentionHudAuthoring.AddTo(hudRoot)` to nest it, stretched and at scale 1. If you nest it by hand, keep its root at scale 1: nested, it is a sub-canvas and nothing drives its scale. Then:

- Open the prefab instance and set its placements in the Inspector, or call `hud.SetPlacement(RetentionHudPart.LevelChip, …)`.
- Call `hud.SetHidden(true)` during cutscenes and menus. Toasts and the summary are queued until the HUD is shown again.

**Rules to keep:**

- **Route match and challenge rewards through the kit.** It already pays account XP and gold (as `LevelsServer` does), so calling `LevelsServer` for the same reward pays it twice. When a game adopts the kit, delete its end-of-match `LevelsServer.AwardPlayerXPAndGold(connection, xp, gold)` call and pass those amounts as `RewardLine`s to `FinishMatch`. At HEAD (75fe24d) those calls are:
  - `TD_ServerRunner.cs:495`
  - `HS_ServerRunner.cs:371`
  - `TY_ServerRunner.cs:438`
  - `CTF_ServerRunner.cs:544`

  War Valley and Dot Invaders have no such call. Line numbers drift while games are edited, so search for `LevelsServer.Award`.
- **Always `Dispose` the service** in the runner's OnDestroy. A forgotten `Dispose` leaves the `RetentionSnapshotRequest` handler and a `PlayerData.OnPlayerRemoved` handler alive, along with two timers. `Dispose` also applies progress that is still queued.
- **Call `Count` and `Reach` freely.** It is fine to call them on every kill or tick. `AddXp` sends one message per call, so batch XP that comes every tick.
- **Unlock icons are theme icon names**: the file names under `com.ryan.ui/Runtime/UI/Theme/Icons` and `Theme/GameIcons` without the `icon_` prefix (`castle`, `bolt`, `crosshair`…). An unknown name shows a star.
- **Stat keys are free-form.** `"kills"`, `"wave"`, `"builds"` and so on are whatever the game reports to `Count` and `Reach`.
- **Pool size.** A pool needs at least `dailyChallengeCount` entries. Eight or more gives the days some variety.
- **Changing a pool.** It is safe to edit a pool between builds, but an edit can change the day's pick. A challenge that is still picked keeps today's progress, because progress is matched by `id`. Changing a challenge's `id` resets its progress.
- **Unlocks.** Gate unlock content at the moment it is used with `retention.IsUnlocked(conn, "frost_tower")`, or apply it once from `Ready`. `IsUnlocked` is false and `LevelOf` is 1 until then. The server raises `Unlocked` once, as each unlock happens.
- **Mid-match level-ups are celebrated twice, on purpose.** They show as a toast and centre text while playing, then again on the summary card as the bar crosses the level.
- **Games without matches** call `retention.ClaimDailyStreak(conn)` (for example, after a few minutes of play), and call `AddXp` for XP earned during play.
- **Players whose save failed to load** still play. They play on defaults, nothing is saved, and the HUD shows "not saved". Their challenges and streak pay game XP only, with no account XP or gold, since those rewards would come back on every rejoin.

## Tuning knobs

All tuning lives on the serialized `RetentionTuning`. Expose it on the game's server runner. It is cloned and validated when `RetentionRules` is built.

| Knob | Default | Effect |
| --- | --- | --- |
| `levels.maxLevel` | 50 | Level cap (2 to 500). At the cap, XP is still banked but the bar stays full. |
| `levels.firstLevelXp` | 100 | XP from level 1 to level 2. |
| `levels.extraXpPerLevel` | 25 | Linear growth per level. |
| `levels.growth` | 1.0 | Exponential growth per level (1.0 to 1.5) on top of the linear growth. |
| `streak.firstDayXp` / `firstDayGold` | 25 / 10 | Bonus for day 1 of a streak. |
| `streak.xpPerExtraDay` / `goldPerExtraDay` | 15 / 5 | Extra bonus for each further day. |
| `streak.maxBonusDays` | 7 | Streak cap. The bonus stops growing at this day. |
| `streak.lineFormat` | `Daily streak (day {0})` | Summary line for the streak bonus. |
| `dailyChallengeCount` | 3 | Challenges per UTC day (1 to 8). |
| `accountXpScale` | 1.0 | Account XP paid for each point of game XP (0 means game XP only). |

**Challenge reward size.** The `xpReward` on `ChallengeDefinition` sets how much a challenge pays against the curve. Aim for about one level for all three daily challenges together at mid levels.

**How the daily pick works.**

- The pick depends only on the date and the universe id. Every server shows the same challenges on the same day.
- Challenges that share a stat key are avoided, so a day doesn't get two "kills" challenges.
- The picks are shown in order of reward.

**HUD knobs** live on the `RetentionHud` component:

- **Key.** `challengesInput` (an `InputAction`, default `<Keyboard>/j`) opens the panel. Add a gamepad binding in the Inspector if a game wants one. It only works while the Character action map is active, so not while typing.
- **Placements.** Each part has its own placement. The defaults are:
  - Level chip: bottom left.
  - Challenges panel: above the chip.
  - Summary card: left edge, centred.
  - Toasts: top right, under the top bar and below the shared player list (with room for four players).
- **`style`.** Colours are taken from the UI theme when the prefab is built.
- **`feel`.**
  - Sound cues
  - Toast, level-up and summary timings
  - Confetti
  - Punch sizes
- **Text formats** for every string.

## Wire contract

`RetentionProtocol.cs` is **append-only**:

- Never rename, reorder or retype a field.
- Add new fields at the end of a struct.
- Add new broadcasts at the end of the file.

All fields are plain types, strings, or arrays of the view structs. The fields added in try 2 are marked `added in kit v2`. The contract is frozen from try 2 on, so only additions are allowed from here.

## Rebuilding the HUD

There are two ways to rebuild it:

- In the Editor, run **Ryan > Retention > Rebuild HUD**.
- In batch mode, run `-executeMethod RyanAssets.Retention.Editor.RetentionHudAuthoring.BuildBatch`.

Either one regenerates `Generated/RetentionHUD.prefab` from the shared UI theme. Do not hand-edit the generated prefab. Override values on the instance in the game's prefab instead.

**Previewing it.** **Ryan > Retention > Preview HUD** enters Play mode in a throwaway scene and feeds the HUD made-up broadcasts (chip, panel, toasts, two summary cards), with no server. `RetentionHudPreview.Capture(dir)` does the same and saves each step's Game view at 1920x1080 and 1280x720. Nothing is saved to any scene.
