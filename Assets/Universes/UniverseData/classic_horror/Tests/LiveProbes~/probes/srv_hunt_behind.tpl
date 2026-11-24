// SERVER PROBE "hunt behind": staging only. Warps Father Crane DIST m from the nearest investigator, on the far side from
// (AX, AZ) (the way they are running), and sets him on them as if he had just seen them.
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
RyanAssets.Characters.Shared.LocalCharacter target = null;
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values) if (c && !c.IsDead) target = c;
var p = target.transform.position;
var away = new Vector3(p.x - AXf, 0f, p.z - AZf).normalized;
m.Warp(p + away * DISTf);
m.Hunt(target);
return $"crane at {m.transform.position}, player at {p}, {Vector3.Distance(m.transform.position, p):0.0} m, state {m.State}";
