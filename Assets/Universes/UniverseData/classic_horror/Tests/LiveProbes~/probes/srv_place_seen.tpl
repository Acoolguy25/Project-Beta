// SERVER PROBE "place seen": warps Father Crane to a NavMesh spot DIST m (+-4) from the first investigator with a clear line to them, and sets him hunting them (m.Hunt), so the hide trace tests his seen-hide rule, not a player's route.
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
RyanAssets.Characters.Shared.LocalCharacter who = null;
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values) if (c) { who = c; break; }
var eyes = who.transform.position + UnityEngine.Vector3.up * 1.5f;
var agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
var rnd = new System.Random(7);
for (int i = 0; i < 600; i++) {
    float angle = (float)(rnd.NextDouble() * 6.28318), dist = DISTf + (float)(rnd.NextDouble() * 8 - 4);
    var p = who.transform.position + new UnityEngine.Vector3(UnityEngine.Mathf.Sin(angle), 0f, UnityEngine.Mathf.Cos(angle)) * dist;
    if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
    if (UnityEngine.Physics.Linecast(hit.position + UnityEngine.Vector3.up * 1.7f, eyes, out _, ~UnityEngine.LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "Water"), UnityEngine.QueryTriggerInteraction.Ignore)) continue;
    m.Warp(hit.position); m.Resume(); var ch = who; m.Hunt(ch);
    return $"crane placed at {hit.position} gap {UnityEngine.Vector3.Distance(hit.position, who.transform.position):0.0}m, clear line of sight; state {m.State}";
}
return "no unseen spot found";
