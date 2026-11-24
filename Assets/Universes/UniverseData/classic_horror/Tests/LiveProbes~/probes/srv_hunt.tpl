// SERVER PROBE "hunt": staging only. Warps Father Crane near (PX, PY, PZ) and sets him on the nearest investigator, as if he had just seen them.
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
m.Warp(new Vector3(PX, PY, PZ));
RyanAssets.Characters.Shared.LocalCharacter target = null; float best = float.MaxValue;
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values)
    if (c && !c.IsDead) { float d = Vector3.Distance(c.transform.position, m.transform.position); if (d < best) { best = d; target = c; } }
m.Hunt(target);
return $"crane at {m.transform.position}, hunting {target?.Owner.ClientId} {best:0.0} m away, state {m.State}";
