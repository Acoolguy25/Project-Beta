// CLIENT PROBE "hide": the local investigator asks to hide in the nearest hiding place, through the controller's own Send, exactly as E does (nothing is set up on the server).
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var c = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>(UnityEngine.FindObjectsInactive.Include);
var map = (Universes.UniverseData.classic_horror.CH_Map)c.GetType().GetField("map", bf).GetValue(c);
var me = RyanAssets.Characters.Client.CharacterMovement.Local.Body.position;
int best = -1; float bestD = float.MaxValue;
for (int i = 0; i < map.hidingSpots.Length; i++) if (map.hidingSpots[i]) { float d = UnityEngine.Vector3.Distance(map.hidingSpots[i].Exit, me); if (d < bestD) { bestD = d; best = i; } }
c.GetType().GetMethod("Send", bf).Invoke(c, new object[] { Universes.UniverseData.classic_horror.CH_Target.Hide, best });
return $"asked to hide in spot {best} \"{map.hidingSpots[best].label}\" at {map.hidingSpots[best].Exit}, {bestD:0.0} m away";
