// CLIENT BOT "night": plays a whole night through the controller and the panel's entry points (what staging was used is stated in the run's report). Walking drives the movement
// controls along a NavMesh path (as a player's stick would), aiming sets the first-person yaw and pitch, E is the controller's
// own Interact, and the minigames go through the panel's own entry points: Sing (what G H J K do), TurnDial (what A / D do, the
// panel's own Update turns the dial at its dial speed and sends the probes) and Action (E: the cord, the lock's Set). A batchmode
// Editor has no focused window, so Unity drops keyboard events and no key can be pressed. It reads only what the server broadcasts.
//
// Deliberate mistakes (so a missed strike, a wrong note and a wrong lock are seen live): MISS_STRIKE, MISS_NOTE and MISS_LOCK are
// the number of mistakes to make on the first engine / hymn / lock before playing it right.
// Hiding: when Crane is within HIDE_RANGE m it climbs into the nearest hiding place within HIDE_SEARCH m and
// waits until he has been 30 m away for 4 s.
// Everything is logged to LOG. Run with the client's camera on a tiny render texture (cli_prep.cs) so frames stay quick.
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
const string KEY = "chnight";
var old = System.AppDomain.CurrentDomain.GetData(KEY) as UnityEditor.EditorApplication.CallbackFunction;
if (old != null) { UnityEditor.EditorApplication.update -= old; System.AppDomain.CurrentDomain.SetData(KEY, null); }
string log = "LOG";
int missStrike = MISS_STRIKE, missNote = MISS_NOTE, missLock = MISS_LOCK; float hideRange = HIDE_RANGE, hideSearch = HIDE_SEARCH;
var ctl = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var ct = ctl.GetType();
var fState = ct.GetField("state", bf); var fHas = ct.GetField("hasState", bf); var fMap = ct.GetField("map", bf);
var mInteract = ct.GetMethod("Interact", bf); var mCarried = ct.GetMethod("LocalCarried", bf); var pHidden = ct.GetProperty("LocalHidden", bf);
var fReach = ct.GetField("interactionReach", bf); var findObject = ct.GetMethod("FindObject", bf);
var panel = ctl.puzzle; var pt = typeof(Universes.UniverseData.classic_horror.Client.CH_PuzzlePanel);
var fCur = pt.GetField("current", bf); var fSync = pt.GetField("syncedAt", bf); var fPlay = pt.GetField("playPos", bf); var fReplay = pt.GetField("replayAt", bf);
var fDial = pt.GetField("dialAngle", bf); var fStation = pt.GetField("stationId", bf);
var fp = UnityEngine.Object.FindAnyObjectByType<RyanAssets.Cameras.FirstPersonController>(UnityEngine.FindObjectsInactive.Include);
var fYaw = fp.GetType().GetField("yaw", bf); var fPitch = fp.GetType().GetField("pitch", bf);
var map = fMap.GetValue(ctl); var boardingPoint = (Transform)map.GetType().GetField("boardingPoint").GetValue(map);
var hidingSpots = (Universes.UniverseData.classic_horror.CH_HidingSpot[])map.GetType().GetField("hidingSpots").GetValue(map);
int agentType = 0;
foreach (var s in UnityEngine.Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsSortMode.None)) { agentType = s.agentTypeID; break; }
var filter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agentType, areaMask = UnityEngine.AI.NavMesh.AllAreas };
var path = new UnityEngine.AI.NavMeshPath();
float started = Time.realtimeSinceStartup, nextPath = 0f, nextAct = 0f, stuckAt = 0f, sidestepUntil = 0f, side = 1f, hiddenSince = -1f, calmSince = -1f, nextLog = 0f, lastChange = Time.realtimeSinceStartup;
Vector3 lastPos = Vector3.zero; int corner = 1, sidesteps = 0, interactTries = 0; bool sprinting = true;
string lastGoal = "", lastPhase = ""; string planKey = ""; int strikes = 0, notes = 0;
float strikeCooldown = 0f, noteCooldown = 0f, lockPhaseAt = 0f; var sweep = new System.Collections.Generic.List<(float angle, float warmth)>();
Universes.UniverseData.classic_horror.CH_HidingSpot committed = null; float commitUntil = 0f;
bool wrongNow = false; float nudgeFrom = 0f, nudgeDir = 1f;
int lockStage = 0; float lockTarget = 0f; int lockProgress = -1; float lastDial = -1f; float lockedStationEnds = 0f;
System.Action<string> say = m => System.IO.File.AppendAllText(log, $"{Time.realtimeSinceStartup - started:0.0}s {m}\n");
System.IO.File.AppendAllText(log, $"# night bot started {System.DateTime.UtcNow:HH:mm:ss} MISS strike {missStrike} note {missNote} lock {missLock} hide {hideRange}\n");

System.Func<Universes.UniverseData.classic_horror.CH_StateBroadcast> state = () => (Universes.UniverseData.classic_horror.CH_StateBroadcast)fState.GetValue(ctl);
System.Func<Transform> crane = () => { var st = state(); return st.monsterId >= 0 ? findObject.Invoke(ctl, new object[] { st.monsterId }) as Transform : null; };

// What to do: (name, point to stand near, kind) from the broadcast alone.
System.Func<(string name, Vector3 point, int kind)> plan = () => {
    var st = state();
    var carried = (Universes.UniverseData.classic_horror.CH_ItemKind?)mCarried.Invoke(ctl, null);
    if (st.phase == Universes.UniverseData.classic_horror.CH_Phase.Boarding) return ("board", boardingPoint.position, 3);
    if (carried.HasValue && st.slots != null) {
        // the open slot that takes it, and for a relic the grave of the singer it belonged to
        Universes.UniverseData.classic_horror.CH_ItemView? held = null;
        var me = RyanAssets.Characters.Client.LocalPlayer.Character;
        foreach (var it in st.items) if (it.state == Universes.UniverseData.classic_horror.CH_ItemState.Carried && it.kind == carried.Value && (held == null || it.carrierId == me.NetworkObject.ObjectId)) held = it;
        foreach (var s in st.slots)
            if (s.open && s.accepts == carried.Value && (s.singer < 0 || held == null || s.singer == held.Value.singer))
                return ("slot " + s.id + " " + s.kind, Universes.UniverseData.classic_horror.CH_Night.SlotReachPoint(s.kind, s.position), 1);
    }
    if (!carried.HasValue && st.stations != null)
        foreach (var s in st.stations) if (s.available && !s.done) return ("station " + s.id + " " + s.kind, Universes.UniverseData.classic_horror.CH_Night.StationReachPoint(s.kind, s.position), 2);
    if (!carried.HasValue && st.items != null) {
        var me = RyanAssets.Characters.Client.LocalPlayer.Character; Vector3 at = me.transform.position; float best = 1e9f; Universes.UniverseData.classic_horror.CH_ItemView? pick = null;
        foreach (var it in st.items) {
            if (it.state != Universes.UniverseData.classic_horror.CH_ItemState.Resting || !it.objective) continue;
            float d = Vector3.Distance(at, it.position); if (d < best) { best = d; pick = it; }
        }
        if (pick.HasValue) return ("item " + pick.Value.id + " " + pick.Value.kind, pick.Value.position + Vector3.up * 0.3f, 0);
    }
    return ("wait", Vector3.zero, -1);
};

UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!Application.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    float now = Time.realtimeSinceStartup;
    var mv = RyanAssets.Characters.Client.CharacterMovement.Local;
    var me = RyanAssets.Characters.Client.LocalPlayer.Character;
    if (mv == null || me == null || mv.Controls == null || !(bool)fHas.GetValue(ctl)) return;
    var st = state();
    if (st.phase.ToString() != lastPhase) { lastPhase = st.phase.ToString(); say($"PHASE {lastPhase} objective \"{st.objective}\" / \"{st.detail}\" lives lost {st.livesLost}"); }
    if (st.phase == Universes.UniverseData.classic_horror.CH_Phase.Complete || st.phase == Universes.UniverseData.classic_horror.CH_Phase.Failed) {
        mv.Controls.move = Vector2.zero; mv.Controls.sprint = false;
        say($"END phase {st.phase} ending {st.ending} escapes {st.escapes}"); UnityEditor.EditorApplication.update -= tick; System.AppDomain.CurrentDomain.SetData(KEY, null); return;
    }
    Vector3 at = me.transform.position;
    bool hidden = (bool)pHidden.GetValue(ctl);
    Transform him = crane(); float gap = him != null ? Vector3.Distance(him.position, at) : 999f;

    // ---- a minigame is open: work it with the keyboard ----
    if (panel.IsOpen) {
        mv.Controls.move = Vector2.zero; mv.Controls.sprint = false;
        var cur = (Universes.UniverseData.classic_horror.CH_PuzzleBroadcast)fCur.GetValue(panel);
        if (nextAct > now) return;
        if (cur.kind == Universes.UniverseData.classic_horror.CH_PuzzleKind.Engine) {
            float pos = Universes.UniverseData.classic_horror.CH_PuzzleRun.NeedleAt(Time.unscaledTime - (float)fSync.GetValue(panel), cur.sweepSpeed);
            bool wantMiss = missStrike > 0;
            float off = Mathf.Abs(pos - cur.zoneCenter);
            if (Time.unscaledTime > strikeCooldown && ((!wantMiss && off < cur.zoneWidth * 0.12f) || (wantMiss && off > cur.zoneWidth * 2.2f && off > 0.3f))) {
                strikeCooldown = Time.unscaledTime + 0.7f; if (wantMiss) missStrike--;
                say($"ENGINE press E needle {pos:0.00} zone {cur.zoneCenter:0.00} width {cur.zoneWidth:0.00} progress {cur.progress}{(wantMiss ? " (deliberate miss)" : "")}");
                panel.Action();
            }
        } else if (cur.kind == Universes.UniverseData.classic_horror.CH_PuzzleKind.Hymn && cur.sequence != null) {
            if (panel.Listening || cur.progress >= cur.sequence.Length) return;
            if (Time.unscaledTime < noteCooldown) return;
            int want = cur.sequence[cur.progress]; int note = want;
            if (missNote > 0 && cur.progress == 1) { note = (want + 1) % 4; missNote--; }
            noteCooldown = Time.unscaledTime + 0.55f;
            say($"HYMN note {cur.progress + 1}/{cur.sequence.Length}: key {Universes.UniverseData.classic_horror.CH_Story.HymnKeys[note]}{(note != want ? " (deliberate wrong note)" : "")}");
            panel.Sing(note);
        } else if (cur.kind == Universes.UniverseData.classic_horror.CH_PuzzleKind.BellLock) {
            float dial = (float)fDial.GetValue(panel);
            if (cur.progress != lockProgress) { lockProgress = cur.progress; lockStage = 0; sweep.Clear(); lastDial = dial; lockPhaseAt = Time.unscaledTime; }
            if (lockStage == 0) { // turn right (D) one full circle, noting the warmth the server answers with
                panel.TurnDial(1f); sweep.Add((dial, cur.warmth));
                if (Time.unscaledTime - lockPhaseAt > 5.5f) { panel.TurnDial(0f);
                    float bestW = -1f, bestA = 0f; foreach (var (a, w) in sweep) if (w > bestW) { bestW = w; bestA = a; }
                    // the hot arc is a plateau: aim at the middle of every sample within 1% of the best
                    float sx = 0f, sy = 0f; int n = 0; foreach (var (a, w) in sweep) if (w >= bestW - 0.01f) { sx += Mathf.Cos(a * Mathf.Deg2Rad); sy += Mathf.Sin(a * Mathf.Deg2Rad); n++; }
                    lockTarget = n > 0 ? Mathf.Repeat(Mathf.Atan2(sy, sx) * Mathf.Rad2Deg, 360f) : bestA;
                    if (missLock > 0) { lockTarget = Mathf.Repeat(lockTarget + 150f, 360f); missLock--; wrongNow = true; say($"LOCK deliberate wrong setting at {lockTarget:0}"); }
                    say($"LOCK warmest {bestW:0.00} at {bestA:0}, aiming {lockTarget:0}");
                    lockStage = 1;
                }
            } else if (lockStage == 1) { // turn the short way to it
                float delta = Mathf.DeltaAngle(dial, lockTarget);
                if (Mathf.Abs(delta) < 1.5f) { lockStage = 2; lockPhaseAt = Time.unscaledTime; panel.TurnDial(0f); }
                else panel.TurnDial(delta > 0f ? 1f : -1f);
            } else if (lockStage == 2 && Time.unscaledTime - lockPhaseAt > 0.8f) {
                // stopped at the aim: set only what the readout calls HOT (a moving dial's warmth lags by the round trip); otherwise nudge and read again
                if (panel.ReadsHot || wrongNow) {
                    say($"LOCK set at {dial:0} warmth {cur.warmth:0.00}{(wrongNow ? " (deliberate wrong setting)" : "")}"); wrongNow = false; panel.Action(); lockStage = 3; lockPhaseAt = Time.unscaledTime;
                } else { nudgeFrom = cur.warmth; lockStage = 5; lockPhaseAt = Time.unscaledTime; say($"LOCK reads {cur.warmth:0.00} at {dial:0}, nudging {(nudgeDir > 0 ? "right" : "left")}"); }
            } else if (lockStage == 5) {
                panel.TurnDial(nudgeDir);
                if (Time.unscaledTime - lockPhaseAt > 0.12f) { panel.TurnDial(0f); lockStage = 6; lockPhaseAt = Time.unscaledTime; }
            } else if (lockStage == 6 && Time.unscaledTime - lockPhaseAt > 0.8f) {
                if (cur.warmth < nudgeFrom - 0.001f) nudgeDir = -nudgeDir; // it got colder: the other way
                lockStage = 2; lockPhaseAt = Time.unscaledTime - 1f;
            } else if (lockStage == 3 && Time.unscaledTime - lockPhaseAt > 1.5f) { lockStage = 0; sweep.Clear(); lockPhaseAt = Time.unscaledTime; } // wrong: try the next sweep
        }
        return;
    }
    lockProgress = -1;

    // ---- hiding when he is close ----
    // once he has made it run for cover it commits to that spot for a while, so it does not dither between the errand and the hiding place
    bool chased = him != null && (gap < hideRange || (committed != null && now < commitUntil && gap < hideRange + 15f));
    if (hidden) {
        mv.Controls.move = Vector2.zero; mv.Controls.sprint = false;
        if (hiddenSince < 0f) { hiddenSince = now; calmSince = -1f; say($"HIDDEN, Crane {gap:0} m"); }
        if (gap > 30f) { if (calmSince < 0f) calmSince = now; } else calmSince = -1f;
        if ((calmSince > 0f && now - calmSince > 4f) || now - hiddenSince > 45f) {
            say($"leaving cover, Crane {gap:0} m"); hiddenSince = -1f; mInteract.Invoke(ctl, null); nextAct = now + 1f;
        }
        return;
    }
    hiddenSince = -1f;
    (string name, Vector3 point, int kind) goal = plan();
    Universes.UniverseData.classic_horror.CH_HidingSpot spot = null;
    if (chased) {
        float best = hideSearch;
        if (committed != null && now < commitUntil) spot = committed;
        else foreach (var h in hidingSpots) { if (h == null) continue; float d = Vector3.Distance(at, h.Handle); if (d < best) { best = d; spot = h; } }
        if (spot != null) { if (spot != committed) { committed = spot; commitUntil = now + 14f; } goal = ("hide " + spot.name, spot.Handle, 4); }
    }
    if (goal.kind < 0) { mv.Controls.move = Vector2.zero; mv.Controls.sprint = false; return; }
    if (goal.name != lastGoal) { lastGoal = goal.name; interactTries = 0; say($"GOAL {goal.name} at {goal.point}"); lastChange = now; }

    // ---- walk to it ----
    Vector3 flat = goal.point - at; flat.y = 0f;
    var eye = Camera.main != null ? Camera.main.transform.position : at + Vector3.up * 1.6f;
    float reach = (float)fReach.GetValue(ctl);
    bool closeEnough = Vector3.Distance(eye, goal.point) < Mathf.Min(reach - 0.8f, 2.0f) || (goal.kind == 3 && flat.magnitude < 2.5f);
    if (now >= nextPath) {
        nextPath = now + 0.5f;
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var a, 3f, filter) && UnityEngine.AI.NavMesh.SamplePosition(goal.point, out var b, 4f, filter)
            && UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, filter, path)) corner = 1;
    }
    if (!closeEnough) {
        Vector3 next = goal.point;
        if (path.corners.Length > 1) {
            while (corner < path.corners.Length - 1 && Vector2.Distance(new Vector2(at.x, at.z), new Vector2(path.corners[corner].x, path.corners[corner].z)) < 1.2f) corner++;
            next = path.corners[Mathf.Min(corner, path.corners.Length - 1)];
            if (corner < path.corners.Length - 1 && UnityEngine.AI.NavMesh.FindClosestEdge(next, out var edge, filter) && edge.distance < 1.1f)
                next += new Vector3(edge.normal.x, 0f, edge.normal.z).normalized * (1.1f - edge.distance);
        }
        Vector3 d = next - at; d.y = 0f;
        float stamina = RyanAssets.Characters.Client.StaminaController.Stamina, max = Mathf.Max(1f, RyanAssets.Characters.Client.StaminaController.MaxStamina);
        if (sprinting && stamina < 1f) sprinting = false; else if (!sprinting && stamina >= max * 0.6f) sprinting = true;
        if (d.sqrMagnitude > 0.01f) {
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; fYaw.SetValue(fp, yaw); fPitch.SetValue(fp, 0f);
        }
        if (now >= stuckAt) {
            if (stuckAt > 0f && now >= sidestepUntil && Vector3.Distance(at, lastPos) < 0.4f) { sidestepUntil = now + 0.45f; side = -side; sidesteps++; }
            lastPos = at; stuckAt = now + 0.6f;
        }
        mv.Controls.move = now < sidestepUntil ? new Vector2(side, 0.25f).normalized : new Vector2(0f, 1f);
        mv.Controls.sprint = sprinting && (chased || goal.kind == 3 || flat.magnitude > 8f);
    } else {
        mv.Controls.move = Vector2.zero; mv.Controls.sprint = false;
        // ---- aim and use ----
        Vector3 look = goal.point - eye; look.y = Mathf.Min(look.y, look.magnitude);
        fYaw.SetValue(fp, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg);
        fPitch.SetValue(fp, -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
        if (goal.kind == 3) return; // boarding: just stand on the boarding point
        if (now >= nextAct) {
            nextAct = now + 0.9f; interactTries++;
            say($"INTERACT {goal.name} (try {interactTries}) from {Vector3.Distance(eye, goal.point):0.0} m");
            mInteract.Invoke(ctl, null);
            if (interactTries > 6) { // not taking: step closer by walking at it for a moment
                sidestepUntil = now + 0.6f; interactTries = 0; say("nudging closer");
            }
        }
    }
    if (now >= nextLog) {
        nextLog = now + 5f;
        say($"pos {at.x:0},{at.z:0} goal {goal.name} {Vector3.Distance(at, goal.point):0}m crane {gap:0}m stamina {RyanAssets.Characters.Client.StaminaController.Stamina:0} sidesteps {sidesteps}");
    }
};
UnityEditor.EditorApplication.update += tick;
System.AppDomain.CurrentDomain.SetData(KEY, tick);
return "night bot armed";
