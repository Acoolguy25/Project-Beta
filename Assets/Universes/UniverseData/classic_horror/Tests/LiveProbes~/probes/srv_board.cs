// SERVER PROBE "board": staging only. Opens the boarding countdown now (the runner's own RunScene(Boarding)) and then
// shortens it to SECS seconds so the left-behind ending can be seen without waiting the full 40.
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
r.GetType().GetMethod("RunScene", bf).Invoke(r, new object[] { Universes.UniverseData.classic_horror.CH_Phase.Boarding });
r.GetType().GetField("boardingEnds", bf).SetValue(r, Time.time + 30f);
var map = r.GetType().GetField("map", bf).GetValue(r);
var bp = (Transform)map.GetType().GetField("boardingPoint").GetValue(map);
return $"boarding open, ends in 30 s; boarding point {bp.position} radius {map.GetType().GetField("boardingRadius").GetValue(map)}";
