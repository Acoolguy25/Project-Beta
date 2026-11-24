// SERVER PROBE "corner": staging only (read-only). Finds chase set-ups for the sight-break trace: an investigator spot P
// with Crane 15-20 m away in plain sight at C, and a goal G 45-75 m of path from P whose last two thirds Crane cannot
// see from C (the route ducks behind a building).
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
int mask = (int)m.GetType().GetField("obstacleMask", bf).GetValue(m);
var agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
var f = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = UnityEngine.AI.NavMesh.AllAreas };
var path = new UnityEngine.AI.NavMeshPath();
System.Func<Vector3, Vector3, float> len = (a, b) => {
    if (!UnityEngine.AI.NavMesh.CalculatePath(a, b, f, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) return -1f;
    float l = 0f; for (int i = 1; i < path.corners.Length; i++) l += Vector3.Distance(path.corners[i - 1], path.corners[i]); return l; };
System.Func<Vector3, Vector3, bool> sees = (a, b) => !Physics.Linecast(a + Vector3.up * 1.7f, b + Vector3.up * 1.2f, mask, QueryTriggerInteraction.Ignore);
System.Func<Vector3, Vector3?> snap = v => UnityEngine.AI.NavMesh.SamplePosition(v, out var h, 2.0f, f) ? h.position : (Vector3?)null;
var sb = new System.Text.StringBuilder(); int found = 0;
for (float x = 530; x <= 610 && found < 6; x += 6)
for (float z = 445; z <= 530 && found < 6; z += 6) {
    var p0 = snap(new Vector3(x, 17f, z)); if (p0 == null) continue; var P = p0.Value;
    for (int a = 0; a < 8 && found < 6; a++) {
        var dir = Quaternion.Euler(0, a * 45f, 0) * Vector3.forward;
        var c0 = snap(P + dir * 17f); if (c0 == null) continue; var C = c0.Value;
        if (Mathf.Abs(C.y - P.y) > 1.5f || !sees(C, P)) continue;
        float cp = len(C, P); if (cp < 0 || cp > 21f) continue;
        for (int b = 0; b < 12; b++) {
            var g0 = snap(P + Quaternion.Euler(0, b * 30f, 0) * Vector3.forward * 40f); if (g0 == null) continue; var G = g0.Value;
            if (Vector3.Dot((G - P).normalized, dir) > -0.2f) continue; // away from him, roughly
            float pg = len(P, G); if (pg < 45f || pg > 75f) continue;
            var corners = path.corners; bool hidden = true; float walked = 0f;
            for (int i = 1; i < corners.Length; i++) { walked += Vector3.Distance(corners[i - 1], corners[i]); if (walked > pg / 3f && sees(C, corners[i])) { hidden = false; break; } }
            if (!hidden || sees(C, G) || corners.Length < 5) continue;
            sb.Append($"P {P.x:0.0} {P.y:0.00} {P.z:0.0} | C {C.x:0.0} {C.y:0.00} {C.z:0.0} ({cp:0.0} m) | G {G.x:0.0} {G.y:0.00} {G.z:0.0} ({pg:0.0} m, {corners.Length} corners)\n");
            found++; break;
        }
    }
}
return found + " found\n" + sb;
