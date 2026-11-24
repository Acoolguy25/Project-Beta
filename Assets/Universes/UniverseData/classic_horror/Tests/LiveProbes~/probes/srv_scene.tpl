// SERVER PROBE "scene": staging only. Replays the cutscene for PHASE with the runner's own PlayCutscene (the night enters
// that phase, Crane is suspended, the scene's lines are broadcast and every client plays it).
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
var token = (System.Threading.CancellationToken)r.GetType().GetField("nightToken", bf).GetValue(r);
r.GetType().GetMethod("PlayCutscene", bf).Invoke(r, new object[] { Universes.UniverseData.classic_horror.CH_Phase.PHASE, token });
return "playing PHASE at " + Time.time;
