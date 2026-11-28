// SERVER PROBE "fit": fits the three boat parts through the runner's own pick-up and place handlers for the first investigator, then lists the stations.
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = r.GetType();
var night = (Universes.UniverseData.classic_horror.CH_Night)t.GetField("night", bf).GetValue(r);
FishNet.Connection.NetworkConnection conn = null; RyanAssets.Characters.Shared.LocalCharacter character = null;
foreach (var kv in RyanAssets.Characters.Shared.LocalCharacter.Characters) { conn = kv.Key; character = kv.Value; break; }
int key = conn.ClientId;
var sb = new System.Text.StringBuilder();
var kinds = new[] { Universes.UniverseData.classic_horror.CH_ItemKind.Fuel, Universes.UniverseData.classic_horror.CH_ItemKind.Toolbox, Universes.UniverseData.classic_horror.CH_ItemKind.Lantern };
foreach (var kind in kinds) {
    Universes.UniverseData.classic_horror.CH_Night.Item item = null;
    foreach (var i in night.Items) if (i.Kind == kind) item = i;
    if (item.State == Universes.UniverseData.classic_horror.CH_ItemState.Placed) continue;
    Universes.UniverseData.classic_horror.CH_Night.Item swapped; bool first;
    if (!night.TryPickUp(key, item.Id, out swapped, out first)) { sb.AppendLine("pick failed " + kind); continue; }
    t.GetMethod("OnPickedUp", bf).Invoke(r, new object[] { conn, character, night.GetItem(item.Id), first });
    Universes.UniverseData.classic_horror.CH_Night.Slot slot = null;
    foreach (var s in night.Slots) if (s.Accepts == kind && s.Kind != Universes.UniverseData.classic_horror.CH_SlotKind.Grave) slot = s;
    Universes.UniverseData.classic_horror.CH_Night.Item placed;
    if (!night.TryPlace(key, slot.Id, out placed)) { sb.AppendLine("place failed " + kind); continue; }
    t.GetMethod("OnPlaced", bf).Invoke(r, new object[] { conn, placed, slot });
}
t.GetMethod("BroadcastState", bf).Invoke(r, null);
sb.AppendLine($"phase {night.Phase} parts fixed {night.PartsFixed}");
foreach (var s in night.Stations) sb.AppendLine($"station {s.Id} {s.Kind} singer {s.Singer} at {s.Position} yaw {s.Yaw} available {night.IsAvailable(s)} done {s.Done} progress {s.Progress}");
return sb.ToString();
