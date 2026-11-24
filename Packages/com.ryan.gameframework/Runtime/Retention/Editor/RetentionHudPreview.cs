using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RyanAssets.Retention.Client;
using RyanAssets.UI.GameHud;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RyanAssets.Retention.Editor {
    /// <summary>
    /// Plays the generated retention HUD in an empty scene made at Play time with made-up data (no server):
    /// the level chip with a streak and an XP gain, the challenges panel, stacked toasts, and two
    /// summary cards. It feeds the HUD the same broadcasts the server sends.
    /// Menu: Ryan > Retention > Preview HUD. <see cref="Capture"/> also saves Game view screenshots at
    /// 1920x1080 and 1280x720 (used for the kit's review shots).
    /// </summary>
    [InitializeOnLoad]
    public static class RetentionHudPreview {
        const string DirKey = "RyanAssets.Retention.Preview.Dir";
        const string ActiveKey = "RyanAssets.Retention.Preview.Active";
        const string ModeKey = "RyanAssets.Retention.Preview.Mode";
        const string FixesMode = "fixes";
        const string PlayerListPrefab = "Packages/com.ryan.hud/Runtime/Client/ClientUI/PlayerList/PlayerListCanvas.prefab";
        const string PlayerRowPrefab = "Packages/com.ryan.hud/Runtime/Client/ClientUI/PlayerList/PlayerListRowPrefab.prefab";
        const string ScreenFxPrefab = "Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Prefabs/ScreenFxOverlay.prefab";
        static readonly Vector2Int[] Resolutions = { new(1920, 1080), new(1280, 720) };

        static IEnumerator routine;
        static double waitUntil;
        static Func<bool> waitFor;
        static double waitForDeadline;
        static readonly StringBuilder notes = new();

        static RetentionHudPreview() {
            // Entering Play mode reloads the domain: pick the run up again from the session state.
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Ryan/Retention/Preview HUD")]
        public static void PreviewMenu() => Start(null);

        /// <summary>Runs the preview and saves each step's Game view to <paramref name="directory"/>.</summary>
        public static void Capture(string directory) => Start(directory);

        /// <summary>
        /// Kit try 3 checks at 1280x720, before and after each fix: the HUD nested in a game canvas,
        /// the toasts beside the shared player list, the card's detail lines, and ClearDanger.
        /// "Before" frames recreate the old state on purpose (scale 0, the old toast offset, the old
        /// label text written into the cells).
        /// </summary>
        public static void CaptureFixes(string directory) => Start(directory, FixesMode);

        static void Start(string directory, string mode = "") {
            SessionState.SetString(ModeKey, mode);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RetentionHudAuthoring.PrefabPath) == null) RetentionHudAuthoring.Build();
            SessionState.SetString(DirKey, directory ?? string.Empty);
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change) {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false)) {
                SessionState.SetBool(ActiveKey, false);
                string dir = SessionState.GetString(DirKey, string.Empty);
                bool fixes = SessionState.GetString(ModeKey, string.Empty) == FixesMode;
                routine = fixes ? RunFixes(string.IsNullOrEmpty(dir) ? null : dir) : Run(string.IsNullOrEmpty(dir) ? null : dir);
                waitUntil = 0;
                waitFor = null;
                EditorApplication.update += Tick;
            } else if (change == PlayModeStateChange.ExitingPlayMode) {
                Stop();
            }
        }

        static void Stop() {
            EditorApplication.update -= Tick;
            routine = null;
        }

        static void Tick() {
            if (routine == null || !EditorApplication.isPlaying) { Stop(); return; }
            double now = EditorApplication.timeSinceStartup;
            if (now < waitUntil) return;
            if (waitFor != null) {
                if (!waitFor() && now < waitForDeadline) return;
                if (now >= waitForDeadline) Debug.LogWarning("[Retention preview] a wait timed out.");
                waitFor = null;
            }
            try {
                if (!routine.MoveNext()) { Stop(); return; }
            } catch (Exception e) {
                Debug.LogException(e);
                Stop();
                return;
            }
            switch (routine.Current) {
                case float seconds: waitUntil = now + seconds; break;
                case Func<bool> condition: waitFor = condition; waitForDeadline = now + 30d; break;
            }
        }

        // ---------------------------------------------------------------- the run

        /// <summary>
        /// The project always plays its start scene (Scene Switcher), so the preview makes its own scene
        /// in Play mode: unloads the others, hides leftover canvases and cameras, adds a camera and the HUD.
        /// </summary>
        static RetentionHud BuildScene(bool nested = false) {
            Scene preview = SceneManager.CreateScene("RetentionPreview");
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene == preview) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) root.SetActive(false);
                SceneManager.UnloadSceneAsync(scene);
            }
            SceneManager.SetActiveScene(preview);
            var probe = new GameObject("probe");
            UnityEngine.Object.DontDestroyOnLoad(probe);
            foreach (GameObject root in probe.scene.GetRootGameObjects())
                if (root != probe && (root.GetComponentInChildren<Canvas>(true) != null || root.GetComponentInChildren<Camera>(true) != null)) root.SetActive(false);
            UnityEngine.Object.Destroy(probe);

            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.2f, 0.26f);
            camera.tag = "MainCamera";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RetentionHudAuthoring.PrefabPath);
            if (!nested) return UnityEngine.Object.Instantiate(prefab).GetComponent<RetentionHud>();
            // As a game nests it (AddTo): a child of the game's own HUD canvas, stretched.
            Transform host = HostCanvas("GameHUD").transform;
            var hud = UnityEngine.Object.Instantiate(prefab, host, false).GetComponent<RetentionHud>();
            Stretch((RectTransform)hud.transform);
            return hud;
        }

        static Canvas HostCanvas(string name) {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // ---------------------------------------------------------------- kit try 3 checks

        static IEnumerator RunFixes(string dir) {
            RetentionHud hud = BuildScene(nested: true);
            var card = Field<RetentionSummaryCard>(hud, "summaryCard");
            var toasts = Field<HudToastFeed>(hud, "toasts");
            notes.Clear();
            if (dir != null) Directory.CreateDirectory(dir);
            const string tag = "1280x720";
            PlayModeWindow.SetCustomRenderingResolution(1280, 720, "Retention fixes " + tag);
            yield return 1f;
            card.Hide();
            toasts.Clear();
            Invoke(hud, "OnSnapshot", Snapshot());
            yield return 0.8f;

            // 1. Nested in a game canvas.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RetentionHudAuthoring.PrefabPath);
            notes.AppendLine($"prefab root localScale = {prefab.transform.localScale}; nested instance localScale = {hud.transform.localScale}");
            hud.transform.localScale = Vector3.zero;
            yield return 0.5f;
            Shot(dir, tag, "1a-before-nested-scale0");
            yield return 0.3f;
            hud.transform.localScale = Vector3.one;
            yield return 0.5f;
            Shot(dir, tag, "1b-after-nested-scale1");
            yield return 0.3f;
            // An instance saved at scale 0 by an old build: RetentionHud.Awake puts it back to 1.
            var holder = new GameObject("InactiveHolder", typeof(RectTransform));
            holder.SetActive(false);
            holder.transform.SetParent(hud.transform.parent, false);
            GameObject old = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            old.transform.localScale = Vector3.zero;
            holder.SetActive(true);
            notes.AppendLine($"old instance forced to scale 0 before Awake: after Awake localScale = {old.transform.localScale}");
            UnityEngine.Object.Destroy(holder);
            yield return 0.3f;

            // 2. Toasts beside the shared player list with four players.
            GameObject list = PlayerList(4);
            RetentionPlacement placement = hud.GetPlacement(RetentionHudPart.Toasts);
            notes.AppendLine($"default toast placement: anchor {placement.anchor}, offset {placement.offset}");
            hud.SetPlacement(RetentionHudPart.Toasts, new RetentionPlacement(new Vector2(1f, 1f), new Vector2(-12f, -60f)));
            yield return 0.3f;
            ThreeToasts(hud);
            yield return 0.9f;
            Shot(dir, tag, "2a-before-toasts-on-player-list");
            yield return 0.3f;
            toasts.Clear();
            hud.SetPlacement(RetentionHudPart.Toasts, placement);
            yield return 0.3f;
            ThreeToasts(hud);
            yield return 0.9f;
            Shot(dir, tag, "2b-after-toasts-below-player-list");
            yield return 0.3f;
            toasts.Clear();
            yield return 2.5f;

            // 3. Detail lines. Before: the label text the old code made, written into the cells.
            Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 0, reason = "Match", level = 9, levelXp = 120, levelXpNeeded = 325, totalXp = 2400 });
            Invoke(hud, "OnSummary", DetailSummary());
            yield return (Func<bool>)(() => Phase(card) == "Done");
            yield return 0.4f;
            RetentionRewardView[] lines = DetailSummary().lines;
            for (int i = 0; i < lines.Length; i++) {
                HudRow row = card.lines.Row(i);
                bool reward = lines[i].xp > 0 || lines[i].gold > 0;
                if (row.cells.Length > 1) row.cells[1].gameObject.SetActive(true);
                if (reward && !string.IsNullOrEmpty(lines[i].detail))
                    row.Set(0, $"{lines[i].label}  <size=85%><alpha=#99>{lines[i].detail}</alpha></size>");
            }
            yield return 0.4f;
            Shot(dir, tag, "3a-before-detail-lines");
            yield return 0.3f;
            card.Hide();
            yield return 0.8f;
            Invoke(hud, "OnSnapshot", Snapshot());
            Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 0, reason = "Match", level = 9, levelXp = 120, levelXpNeeded = 325, totalXp = 2400 });
            Invoke(hud, "OnSummary", DetailSummary());
            yield return (Func<bool>)(() => Phase(card) == "Fill" && Float(card, "travelled") >= 0.5f * Float(card, "distance"));
            Shot(dir, tag, "3b-after-detail-lines-midfill");
            yield return 0.3f;
            yield return (Func<bool>)(() => Phase(card) == "Done");
            yield return 0.6f;
            Shot(dir, tag, "3c-after-detail-lines-done");
            yield return 0.3f;
            card.Hide();
            UnityEngine.Object.Destroy(list);
            yield return 0.8f;

            // 4. ScreenFxOverlay.ClearDanger during a danger flash.
            var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenFxPrefab);
            if (fxPrefab != null) {
                var fx = UnityEngine.Object.Instantiate(fxPrefab).GetComponent<RyanAssets.GameFeel.ScreenFxOverlay>();
                var vignette = Field<UnityEngine.UI.Image>(fx, "dangerVignette");
                fx.DangerFlash(new Color(0.9f, 0.1f, 0.1f), 2f, 0.6f);
                yield return 0.4f;
                notes.AppendLine($"ClearDanger: vignette alpha mid-flash = {vignette.color.a:0.000}");
                Shot(dir, tag, "4a-danger-flash");
                yield return 0.1f;
                fx.ClearDanger();
                yield return 0.6f;
                notes.AppendLine($"ClearDanger: vignette alpha 0.6 s after ClearDanger = {vignette.color.a:0.000}");
                Shot(dir, tag, "4b-after-cleardanger");
                yield return 0.3f;
                UnityEngine.Object.Destroy(fx.gameObject);
            }

            if (dir != null) File.WriteAllText(Path.Combine(dir, "fix-notes.txt"), notes.ToString());
            Debug.Log("[Retention preview] fixes done." + (dir != null ? " Shots in " + dir : string.Empty) + "\n" + notes);
            EditorApplication.isPlaying = false;
        }

        static void ThreeToasts(RetentionHud hud) {
            Invoke(hud, "OnChallenge", new RetentionChallengeBroadcast {
                index = 2, id = "wave_10", text = "Reach wave 10", progress = 10, target = 10, completed = true, xpReward = 150, goldReward = 25, day = Day,
            });
            Invoke(hud, "OnLevelUp", new RetentionLevelUpBroadcast {
                previousLevel = 7, level = 8, unlocks = new[] { Unlock(8, "frost_tower", "Frost Tower", "Slows every enemy in range.", "castle") },
                hasNextUnlock = true, nextUnlock = Unlock(10, "tesla_tower", "Tesla Tower", "Chains lightning between enemies.", "bolt"),
            });
        }

        /// <summary>The shared HUD's player list prefab with <paramref name="rows"/> made-up players (no network).</summary>
        static GameObject PlayerList(int rows) {
            var listPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerListPrefab);
            var rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRowPrefab);
            if (listPrefab == null || rowPrefab == null) {
                Debug.LogWarning("[Retention preview] the shared player list prefabs are missing; toasts shown alone.");
                return null;
            }
            GameObject list = UnityEngine.Object.Instantiate(listPrefab);
            var scroll = list.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            string[] names = { "Editor 9836e3", "Ryan", "Maple", "Quinn" };
            for (int i = 0; i < rows; i++) {
                GameObject row = UnityEngine.Object.Instantiate(rowPrefab, scroll.content, false);
                foreach (TMPro.TMP_Text text in row.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    text.text = text.name == "Name" ? names[i % names.Length] : (10 - i).ToString();
            }
            // The list itself sizes its content as players join; here, size it for the rows by hand.
            var rowLayout = scroll.content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            float spacing = rowLayout != null ? rowLayout.spacing : 2f;
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rows * 34f + (rows - 1) * spacing);
            return list;
        }

        static RetentionSummaryBroadcast DetailSummary() => new() {
            title = "Victory!",
            lines = new[] {
                Line("Victory", 100, 25),
                Line("Stars", 70, 0, 1, "7 / 7"),
                Line("Win streak", 40, 0, 1, "4 wins in a row"),
                Line("Perfect defence", 50, 15, 1, "no leaks"),
                Line("Round points", 0, 0, 1, "420 (best 510)"),
                Line("Daily streak (day 4)", 70, 25),
            },
            totalXp = 330, totalGold = 65,
            levelBefore = 8, levelXpBefore = 150, levelXpNeededBefore = 300,
            levelAfter = 9, levelXpAfter = 120, levelXpNeededAfter = 325,
            unlocks = new[] { Unlock(9, "mortar", "Mortar", "Long range splash damage.", "crosshair") },
            streakDays = 4, hasNextUnlock = true, nextUnlock = Unlock(10, "tesla_tower", "Tesla Tower", "Chains lightning between enemies.", "bolt"),
        };

        static IEnumerator Run(string dir) {
            RetentionHud hud = BuildScene();
            if (hud == null) throw new InvalidOperationException("The retention HUD prefab has no RetentionHud.");
            var card = Field<RetentionSummaryCard>(hud, "summaryCard");
            var toasts = Field<HudToastFeed>(hud, "toasts");
            notes.Clear();
            if (dir != null) Directory.CreateDirectory(dir);

            foreach (Vector2Int resolution in Resolutions) {
                string tag = $"{resolution.x}x{resolution.y}";
                PlayModeWindow.SetCustomRenderingResolution((uint)resolution.x, (uint)resolution.y, "Retention preview " + tag);
                yield return 1f;
                card.Hide();
                hud.ShowChallenges(false);
                toasts.Clear();
                yield return 0.5f;

                // Chip: level 7 with a 3-day streak, then an XP gain.
                Invoke(hud, "OnSnapshot", Snapshot());
                yield return 0.8f;
                Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 45, reason = "Kill", level = 7, levelXp = 165, levelXpNeeded = 275, totalXp = 1765, announce = true });
                yield return 0.25f;
                Shot(dir, tag, "1-chip-streak-xp");
                yield return 1.5f;
                toasts.Clear();

                // Panel: one challenge done, one with a long text.
                hud.ShowChallenges(true);
                yield return 0.8f;
                Shot(dir, tag, "2-panel");
                yield return 0.3f; // the shot is taken at the end of the frame: change nothing before it
                hud.ShowChallenges(false);
                yield return 0.5f;

                // Three stacked toasts: a completed challenge, a level-up and its unlock.
                Invoke(hud, "OnChallenge", new RetentionChallengeBroadcast {
                    index = 2, id = "wave_10", text = "Reach wave 10", progress = 10, target = 10, completed = true, xpReward = 150, goldReward = 25, day = Day,
                });
                yield return 0.3f;
                Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 150, reason = "Reach wave 10", level = 8, levelXp = 40, levelXpNeeded = 300, totalXp = 1915, announce = true });
                Invoke(hud, "OnLevelUp", new RetentionLevelUpBroadcast {
                    previousLevel = 7, level = 8, unlocks = new[] { Unlock(8, "frost_tower", "Frost Tower", "Slows every enemy in range.", "castle") },
                    hasNextUnlock = true, nextUnlock = Unlock(10, "tesla_tower", "Tesla Tower", "Chains lightning between enemies.", "bolt"),
                });
                yield return 0.9f;
                Shot(dir, tag, "3-toasts");
                yield return 2.5f;
                toasts.Clear();
                yield return 0.5f;

                // Summary card: 8 lines, two level-ups, an unlock. The chip already shows the new level,
                // as it would after the match's XP broadcasts.
                Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 0, reason = "Match", level = 10, levelXp = 155, levelXpNeeded = 350, totalXp = 2705 });
                Invoke(hud, "OnSummary", BigSummary());
                yield return (Func<bool>)(() => card.Visible && Int(card, "revealed") >= 2);
                Shot(dir, tag, "4a-card-title-early");
                Note(tag, "title y after 2 lines", card);
                yield return (Func<bool>)(() => Int(card, "revealed") >= 7);
                Shot(dir, tag, "4b-card-title-late");
                Note(tag, "title y after 7 lines", card);
                yield return (Func<bool>)(() => Phase(card) == "Fill" && Float(card, "travelled") >= 0.45f * Float(card, "distance"));
                Shot(dir, tag, "5-card-8-lines-midfill");
                yield return (Func<bool>)(() => Phase(card) == "Done");
                yield return 0.6f;
                Shot(dir, tag, "6-card-levelup-unlock");
                yield return 0.3f; // the shot is taken at the end of the frame: change nothing before it
                Note(tag, "title y when done", card);
                card.Hide();
                yield return 0.8f;

                // A short card with a challenge toast at the same time.
                Invoke(hud, "OnSnapshot", Snapshot());
                Invoke(hud, "OnXp", new RetentionXpBroadcast { xp = 0, reason = "Match", level = 10, levelXp = 255, levelXpNeeded = 350, totalXp = 2805 });
                Invoke(hud, "OnSummary", SmallSummary());
                yield return (Func<bool>)(() => Phase(card) == "Done");
                Invoke(hud, "OnChallenge", new RetentionChallengeBroadcast {
                    index = 1, id = "kills_150", text = LongText, progress = 150, target = 150, completed = true, xpReward = 300, goldReward = 60, day = Day,
                });
                yield return 0.8f;
                Shot(dir, tag, "7-card-and-toast");
                yield return 0.3f; // the shot is taken at the end of the frame: change nothing before it
                card.Hide();
                toasts.Clear();
                yield return 0.8f;
            }

            if (dir != null) File.WriteAllText(Path.Combine(dir, "title-positions.txt"), notes.ToString());
            Debug.Log("[Retention preview] done." + (dir != null ? " Shots in " + dir : string.Empty));
            EditorApplication.isPlaying = false;
        }

        // ---------------------------------------------------------------- made-up data

        const int Day = 20780;
        const string LongText = "Destroy 150 enemies with towers placed on the outer ring while a boss is on the map";

        static RetentionUnlockView Unlock(int level, string id, string title, string description, string icon) =>
            new() { level = level, id = id, title = title, description = description, icon = icon };

        static RetentionSnapshotBroadcast Snapshot() => new() {
            universeId = "preview", level = 7, levelXp = 120, levelXpNeeded = 275, totalXp = 1720, maxLevel = 50,
            challenges = new[] {
                new RetentionChallengeView { id = "wins_1", text = "Win 1 match", progress = 1, target = 1, xpReward = 120, goldReward = 30, done = true },
                new RetentionChallengeView { id = "kills_150", text = LongText, progress = 37, target = 150, xpReward = 300, goldReward = 60 },
                new RetentionChallengeView { id = "wave_10", text = "Reach wave 10", progress = 4, target = 10, xpReward = 150, goldReward = 25 },
            },
            secondsToReset = 5 * 3600 + 23 * 60, streakDays = 3, streakClaimedToday = true, streakNextXp = 70, streakNextGold = 25, bestStreak = 5,
            hasNextUnlock = true, nextUnlock = Unlock(8, "frost_tower", "Frost Tower", "Slows every enemy in range.", "castle"),
            challengeDay = Day,
        };

        static RetentionRewardView Line(string label, int xp, int gold = 0, int count = 1, string detail = null) =>
            new() { label = label, xp = xp, gold = gold, count = count, detail = detail };

        static RetentionSummaryBroadcast BigSummary() => new() {
            title = "Victory!",
            lines = new[] {
                Line("Victory", 100, 25),
                Line("Kill", 230, 0, 46),
                Line("Waves held", 120, 20),
                Line("Boss destroyed", 80, 15),
                Line("Reach wave 10", 150, 25),
                Line("Stars", 0, 0, 1, "3 / 3"),
                Line("Win streak", 40, 0, 1, "4 wins in a row"),
                Line("Daily streak (day 4)", 70, 25),
            },
            totalXp = 790, totalGold = 110,
            levelBefore = 8, levelXpBefore = 40, levelXpNeededBefore = 300,
            levelAfter = 10, levelXpAfter = 155, levelXpNeededAfter = 350,
            unlocks = new[] { Unlock(10, "tesla_tower", "Tesla Tower", "Chains lightning between up to four enemies.", "bolt") },
            streakDays = 4, hasNextUnlock = true, nextUnlock = Unlock(15, "mortar", "Mortar", "Long range splash damage.", "crosshair"),
        };

        static RetentionSummaryBroadcast SmallSummary() => new() {
            title = "Round over",
            lines = new[] { Line("Survived", 60, 10), Line("Kill", 40, 0, 8), Line("Round points", 0, 0, 1, "420 (best 510)") },
            totalXp = 100, totalGold = 10,
            levelBefore = 10, levelXpBefore = 155, levelXpNeededBefore = 350,
            levelAfter = 10, levelXpAfter = 255, levelXpNeededAfter = 350,
            unlocks = Array.Empty<RetentionUnlockView>(),
            hasNextUnlock = true, nextUnlock = Unlock(15, "mortar", "Mortar", "Long range splash damage.", "crosshair"),
        };

        // ---------------------------------------------------------------- helpers

        static void Shot(string dir, string tag, string name) {
            if (dir == null) return;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{name}-{tag}.png"));
        }

        static void Note(string tag, string what, RetentionSummaryCard card) {
            Canvas.ForceUpdateCanvases();
            RectTransform title = card.title.rectTransform;
            notes.AppendLine($"{tag}  {what}: title anchored y = {title.anchoredPosition.y:0.00}, world y = {title.position.y:0.00}, card height = {((RectTransform)card.transform).rect.height:0.0}");
        }

        /// <summary>Calls one of the HUD's broadcast handlers, as FishNet would.</summary>
        static void Invoke<T>(RetentionHud hud, string handler, T message) {
            MethodInfo method = typeof(RetentionHud).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(RetentionHud), handler);
            Type channel = method.GetParameters()[1].ParameterType;
            method.Invoke(hud, new object[] { message, Enum.ToObject(channel, 0) });
        }

        static T Field<T>(object target, string name) =>
            (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingFieldException(target.GetType().Name, name)).GetValue(target);

        static int Int(object target, string name) => Field<int>(target, name);
        static float Float(object target, string name) => Field<float>(target, name);
        static string Phase(RetentionSummaryCard card) => Field<object>(card, "phase").ToString();
    }
}
