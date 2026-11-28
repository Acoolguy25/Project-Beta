// CLIENT PROBE "hymn bot": while a hymn is open, waits out the playback, then sings the next note of the hymn it was shown.
var c = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var p = c.puzzle;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var type = typeof(Universes.UniverseData.classic_horror.Client.CH_PuzzlePanel);
var fCur = type.GetField("current", bf); var fPlay = type.GetField("playPos", bf); var fReplay = type.GetField("replayAt", bf); var fSend = type.GetField("send", bf); var fStation = type.GetField("stationId", bf);
float last = -10f; bool wasOpen = false;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!p.IsOpen) { if (wasOpen) UnityEditor.EditorApplication.update -= tick; return; }
    wasOpen = true;
    var cur = (Universes.UniverseData.classic_horror.CH_PuzzleBroadcast)fCur.GetValue(p);
    if (cur.kind != Universes.UniverseData.classic_horror.CH_PuzzleKind.Hymn || cur.sequence == null) return;
    if ((int)fPlay.GetValue(p) >= 0 || (float)fReplay.GetValue(p) > 0f) return;
    if (Time.unscaledTime - last < 0.45f || cur.progress >= cur.sequence.Length) return;
    last = Time.unscaledTime;
    var send = (System.Func<Universes.UniverseData.classic_horror.CH_PuzzleAction, int, float, bool>)fSend.GetValue(p);
    send(Universes.UniverseData.classic_horror.CH_PuzzleAction.Press, (int)fStation.GetValue(p), cur.sequence[cur.progress]);
};
UnityEditor.EditorApplication.update += tick;
return "hymn bot armed";
