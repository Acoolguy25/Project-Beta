// CLIENT PROBE "engine bot": while the engine minigame is open, pulls the cord each time the needle crosses the zone's middle.
var c = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var p = c.puzzle;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var type = typeof(Universes.UniverseData.classic_horror.Client.CH_PuzzlePanel);
var fCur = type.GetField("current", bf); var fSync = type.GetField("syncedAt", bf);
float last = -10f; bool wasOpen = false;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!p.IsOpen) { if (wasOpen) UnityEditor.EditorApplication.update -= tick; return; }
    wasOpen = true;
    var cur = (Universes.UniverseData.classic_horror.CH_PuzzleBroadcast)fCur.GetValue(p);
    if (cur.kind != Universes.UniverseData.classic_horror.CH_PuzzleKind.Engine) return;
    float pos = Universes.UniverseData.classic_horror.CH_PuzzleRun.NeedleAt(Time.unscaledTime - (float)fSync.GetValue(p), cur.sweepSpeed);
    if (Mathf.Abs(pos - cur.zoneCenter) < cur.zoneWidth * 0.15f && Time.unscaledTime - last > 0.5f) { last = Time.unscaledTime; p.Action(); }
};
UnityEditor.EditorApplication.update += tick;
return "bot armed";
