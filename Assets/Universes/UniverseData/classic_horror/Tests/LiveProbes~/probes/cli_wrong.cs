// CLIENT PROBE "wrong": sings one wrong note into the open hymn (the one after what the hymn calls for).
var c = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var p = c.puzzle;
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var type = typeof(Universes.UniverseData.classic_horror.Client.CH_PuzzlePanel);
var cur = (Universes.UniverseData.classic_horror.CH_PuzzleBroadcast)type.GetField("current", bf).GetValue(p);
var send = (System.Func<Universes.UniverseData.classic_horror.CH_PuzzleAction, int, float, bool>)type.GetField("send", bf).GetValue(p);
int wrong = (cur.sequence[cur.progress] + 1) % 4;
bool ok = send(Universes.UniverseData.classic_horror.CH_PuzzleAction.Press, (int)type.GetField("stationId", bf).GetValue(p), wrong);
return "open " + p.IsOpen + " sent wrong note " + wrong + " (wanted " + cur.sequence[cur.progress] + ") ok " + ok;
