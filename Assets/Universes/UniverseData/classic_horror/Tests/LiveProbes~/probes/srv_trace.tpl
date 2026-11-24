// SERVER PROBE "trace": every 0.5 s of wall clock, logs Father Crane's mode, the straight and path gap to the nearest
// investigator and whether he has a clear line from his eyes to them, into LOG. While nobody is alive the gap reads "-". "stop" as LOG ends it.
var key = "chtrace";
var old = System.AppDomain.CurrentDomain.GetData(key) as UnityEditor.EditorApplication.CallbackFunction;
if (old != null) { UnityEditor.EditorApplication.update -= old; System.AppDomain.CurrentDomain.SetData(key, null); }
if ("LOG" == "stop") return "stopped";
string log = "LOG";
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
var agent = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
var path = new UnityEngine.AI.NavMeshPath();
int mask = (int)m.GetType().GetField("obstacleMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(m);
float started = Time.realtimeSinceStartup, nextLog = 0f;
System.IO.File.AppendAllText(log, $"# trace {System.DateTime.UtcNow:HH:mm:ss}\n");
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!Application.isPlaying || m == null) { UnityEditor.EditorApplication.update -= tick; return; }
    float now = Time.realtimeSinceStartup;
    if (now < nextLog) return;
    nextLog = now + 0.5f;
    RyanAssets.Characters.Shared.LocalCharacter nearest = null; float best = float.MaxValue;
    foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values)
        if (c && !c.IsDead) { float dist = Vector3.Distance(c.transform.position, m.transform.position); if (dist < best) { best = dist; nearest = c; } }
    float pathGap = -1f;
    if (nearest != null && agent != null && agent.isOnNavMesh && UnityEngine.AI.NavMesh.SamplePosition(nearest.transform.position, out var hit, 3f, new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = UnityEngine.AI.NavMesh.AllAreas })
        && UnityEngine.AI.NavMesh.CalculatePath(m.transform.position, hit.position, new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = UnityEngine.AI.NavMesh.AllAreas }, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
        pathGap = 0f; for (int i = 1; i < path.corners.Length; i++) pathGap += Vector3.Distance(path.corners[i - 1], path.corners[i]);
    }
    bool sight = nearest != null && !Physics.Linecast(m.transform.position + Vector3.up * 1.7f, nearest.transform.position + Vector3.up * 1.2f, mask, QueryTriggerInteraction.Ignore);
    string gap = nearest != null ? $"{best:0.0}m" : "-";
    string p = nearest != null ? $"{nearest.transform.position.x:0.0},{nearest.transform.position.z:0.0}" : "-";
    System.IO.File.AppendAllText(log, $"{now - started:0.0}s t={Time.time:0.0} crane {m.State} {m.transform.position.x:0.0},{m.transform.position.z:0.0} speed {(agent ? agent.velocity.magnitude : 0f):0.0} set {(agent ? agent.speed : 0f):0.0} want {(agent ? agent.desiredVelocity.magnitude : 0f):0.0} link {(agent && agent.isOnOffMeshLink)} corners {(agent && agent.hasPath ? agent.path.corners.Length : 0)} | player {p} gap {gap} path {pathGap:0.0}m sight {(sight ? "yes" : "no")}\n");
};
UnityEditor.EditorApplication.update += tick;
System.AppDomain.CurrentDomain.SetData(key, tick);
return "tracing to " + log;
