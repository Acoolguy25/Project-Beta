// CLIENT PROBE "lock bot": while the bell's locks are open, feels round the dial (a probe every 0.15 s, reading the warmth the server answers with), then sets each ring where it was warmest.
var c = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var p = c.puzzle;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var type = typeof(Universes.UniverseData.classic_horror.Client.CH_PuzzlePanel);
var fCur = type.GetField("current", bf); var fSend = type.GetField("send", bf); var fStation = type.GetField("stationId", bf);
var send = (System.Func<Universes.UniverseData.classic_horror.CH_PuzzleAction, int, float, bool>)fSend.GetValue(p);
float last = -10f, bestAngle = 0f, bestWarmth = -1f, angle = 0f, step = 12f, from = 0f, to = 360f; int phase = 0, progressSeen = -1; bool wasOpen = false; float awaiting = -1f;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!p.IsOpen) { if (wasOpen) UnityEditor.EditorApplication.update -= tick; return; }
    wasOpen = true;
    var cur = (Universes.UniverseData.classic_horror.CH_PuzzleBroadcast)fCur.GetValue(p);
    if (cur.kind != Universes.UniverseData.classic_horror.CH_PuzzleKind.BellLock) return;
    if (cur.progress != progressSeen) { progressSeen = cur.progress; phase = 0; angle = 0f; from = 0f; to = 360f; step = 12f; bestWarmth = -1f; awaiting = -1f; }
    if (Time.unscaledTime - last < 0.15f) return;
    last = Time.unscaledTime;
    int station = (int)fStation.GetValue(p);
    if (awaiting >= 0f) { if (cur.warmth > bestWarmth) { bestWarmth = cur.warmth; bestAngle = awaiting; } awaiting = -1f; }
    if (phase == 0 || phase == 1) {
        if (angle <= to) { awaiting = angle; send(Universes.UniverseData.classic_horror.CH_PuzzleAction.Probe, station, angle); angle += step; return; }
        if (phase == 0) { phase = 1; from = bestAngle - 12f; to = bestAngle + 12f; angle = from; step = 2f; bestWarmth = -1f; return; }
        phase = 2; return;
    }
    if (phase == 2) { send(Universes.UniverseData.classic_horror.CH_PuzzleAction.Set, station, (bestAngle + 360f) % 360f); phase = 3; }
};
UnityEditor.EditorApplication.update += tick;
return "lock bot armed";
