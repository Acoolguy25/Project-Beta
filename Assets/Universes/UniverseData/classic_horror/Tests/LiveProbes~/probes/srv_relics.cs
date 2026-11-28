// SERVER PROBE "relics": lays every relic on its singer's grave through the runner's own handlers for the first investigator, then lists the hymn stations.
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var t = r.GetType();
var night = (Universes.UniverseData.classic_horror.CH_Night)t.GetField("night", bf).GetValue(r);
FishNet.Connection.NetworkConnection conn = null; RyanAssets.Characters.Shared.LocalCharacter character = null;
foreach (var kv in RyanAssets.Characters.Shared.LocalCharacter.Characters) { conn = kv.Key; character = kv.Value; break; }
int key = conn.ClientId;
var sb = new System.Text.StringBuilder();
foreach (var item in night.Items) {
    if (item.Singer < 0 || item.State == Universes.UniverseData.classic_horror.CH_ItemState.Placed || item.State == Universes.UniverseData.classic_horror.CH_ItemState.Hidden) continue;
    Universes.UniverseData.classic_horror.CH_Night.Item swapped; bool first;
    if (!night.TryPickUp(key, item.Id, out swapped, out first)) { sb.AppendLine("pick failed " + item.Kind); continue; }
    t.GetMethod("OnPickedUp", bf).Invoke(r, new object[] { conn, character, night.GetItem(item.Id), first });
    Universes.UniverseData.classic_horror.CH_Night.Slot slot = null;
    foreach (var s in night.Slots) if (s.Kind == Universes.UniverseData.classic_horror.CH_SlotKind.Grave && s.Singer == item.Singer) slot = s;
    Universes.UniverseData.classic_horror.CH_Night.Item placed;
    if (!night.TryPlace(key, slot.Id, out placed)) { sb.AppendLine("place failed " + item.Kind); continue; }
    t.GetMethod("OnPlaced", bf).Invoke(r, new object[] { conn, placed, slot });
}
t.GetMethod("BroadcastState", bf).Invoke(r, null);
foreach (var s in night.Stations) if (s.Kind == Universes.UniverseData.classic_horror.CH_PuzzleKind.Hymn) sb.AppendLine($"station {s.Id} singer {s.Singer} tonight {night.IsSingerTonight(s.Singer)} at {s.Position} available {night.IsAvailable(s)} done {s.Done}");
return sb.ToString();
