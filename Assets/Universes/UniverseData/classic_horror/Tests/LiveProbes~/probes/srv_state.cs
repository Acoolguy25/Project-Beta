// SERVER PROBE (read-only): phase, items, monster mode.
var r = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_ServerRunner>();
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var night = (Universes.UniverseData.classic_horror.CH_Night)r.GetType().GetField("night", bf).GetValue(r);
var sb = new System.Text.StringBuilder();
sb.Append($"phase {night.Phase} t={Time.time:0} monster {(m ? m.State + " at " + m.transform.position : "none")} chases {m?.ChasesStarted}/{m?.ChasesEscaped} catches {m?.Catches}\n");
foreach (var it in night.Items) sb.Append($"item {it.Id} {it.Kind} {it.State} {it.Position} carrier {it.Carrier}\n");
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values) if (c) sb.Append($"player {c.Owner.ClientId} at {c.transform.position} dead {c.IsDead}\n");
return sb.ToString();
