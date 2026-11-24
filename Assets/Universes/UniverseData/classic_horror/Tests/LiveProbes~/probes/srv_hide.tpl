// SERVER PROBE "hide": staging only. Puts the investigator into the nearest hiding place (or takes them out) exactly as
// pressing E does, through CH_ServerRunner.ToggleHiding. With DELAY > 0 it first waits until Father Crane has seen them
// (his lastSeen entry is fresh) and then waits DELAY more seconds, so "he watched you climb in" can be staged without
// the client's input latency.
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
RyanAssets.Characters.Shared.LocalCharacter ch = null;
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values) if (c && !c.IsDead) ch = c;
var map = r.GetType().GetField("map", bf).GetValue(r);
var spots = (Universes.UniverseData.classic_horror.CH_HidingSpot[])map.GetType().GetField("hidingSpots").GetValue(map);
int best = -1; float bestD = float.MaxValue;
for (int i = 0; i < spots.Length; i++) if (spots[i]) { float d = Vector3.Distance(spots[i].Exit, ch.transform.position); if (d < bestD) { bestD = d; best = i; } }
var toggle = r.GetType().GetMethod("ToggleHiding", bf);
var seen = (System.Collections.IDictionary)m.GetType().GetField("lastSeen", bf).GetValue(m);
seen.Remove(ch);
float delay = DELAYf, seenAt = -1f;
if (delay <= 0f) { toggle.Invoke(r, new object[] { ch.Owner, ch, best }); return $"toggled spot {best} {spots[best].name}, hidden {r.IsHidden(ch)}"; }
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!Application.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    if (seenAt < 0f) { if (seen.Contains(ch)) seenAt = Time.time; return; }
    if (Time.time - seenAt < delay) return;
    UnityEditor.EditorApplication.update -= tick;
    toggle.Invoke(r, new object[] { ch.Owner, ch, best });
    Debug.Log($"[stage] hid in {spots[best].name} {Time.time - seenAt:0.00}s after he saw them, hidden {r.IsHidden(ch)}, crane {m.State} {Vector3.Distance(m.transform.position, ch.transform.position):0.0} m");
};
UnityEditor.EditorApplication.update += tick;
return $"waiting to hide in {spots[best].name}";
