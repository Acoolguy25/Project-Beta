// CLIENT BOT "run" (works with the cameras off too): the local investigator runs a NavMesh path to (GX, GY, GZ) at real speed, sprinting like a careful
// player (sprint until empty, walk until stamina is back to AGAIN %, sprint again). With DROPAT > 0 it presses R (puts the
// carried item down) the first time Crane comes within DROPAT m. With WALKAFTER > 0 it stops sprinting for good after that
// many seconds (a player who has broken his line of sight and goes quiet). Logs every 0.5 s to LOG, with his distance as this client
// sees it. GX=stop ends it.
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var key = "chbot";
var old = System.AppDomain.CurrentDomain.GetData(key) as UnityEditor.EditorApplication.CallbackFunction;
if (old != null) { UnityEditor.EditorApplication.update -= old; System.AppDomain.CurrentDomain.SetData(key, null); }
var mv0 = RyanAssets.Characters.Client.CharacterMovement.Local;
if (mv0 != null && mv0.Controls != null) { mv0.Controls.move = Vector2.zero; mv0.Controls.sprint = false; }
if ("GX" == "stop") return "stopped";
Vector3 goal = new Vector3(GX, GY, GZ);
string log = "LOG";
System.IO.File.AppendAllText(log, $"# run to {goal} at {System.DateTime.UtcNow:HH:mm:ss}\n");
var fp = UnityEngine.Object.FindAnyObjectByType<RyanAssets.Cameras.FirstPersonController>(FindObjectsInactive.Include);
var yawField = fp.GetType().GetField("yaw", bf);
var path = new UnityEngine.AI.NavMeshPath();
var ctl = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>();
var stateField = ctl.GetType().GetField("state", bf);
var findObject = ctl.GetType().GetMethod("FindObject", bf);
var putDown = ctl.GetType().GetMethod("PutDown", bf);
bool dropped = false;
System.Func<Transform> crane = () => {
    var st = stateField.GetValue(ctl);
    int id = (int)st.GetType().GetField("monsterId").GetValue(st);
    return id >= 0 ? findObject.Invoke(ctl, new object[] { id }) as Transform : null;
};
int agentType = 0;
foreach (var s in UnityEngine.Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsSortMode.None)) { agentType = s.agentTypeID; break; }
var filter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = agentType, areaMask = UnityEngine.AI.NavMesh.AllAreas };
float nextPath = 0f, nextLog = 0f, started = Time.realtimeSinceStartup; int corner = 1; bool sprinting = true; int frames = 0;
Vector3 lastAt = Vector3.zero; float stuckCheck = 0f, sidestepUntil = 0f, side = 1f; int sidesteps = 0;
UnityEditor.EditorApplication.CallbackFunction tick = null;
tick = () => {
    if (!Application.isPlaying) { UnityEditor.EditorApplication.update -= tick; return; }
    var mv = RyanAssets.Characters.Client.CharacterMovement.Local;
    var me = RyanAssets.Characters.Client.LocalPlayer.Character;
    if (mv == null || me == null || mv.Controls == null) return;
    frames++;
    float now = Time.realtimeSinceStartup;
    Vector3 at = me.transform.position;
    if (now >= nextPath) {
        nextPath = now + 0.5f;
        if (UnityEngine.AI.NavMesh.SamplePosition(at, out var a, 3f, filter) && UnityEngine.AI.NavMesh.SamplePosition(goal, out var b, 3f, filter)
            && UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, filter, path)) corner = 1;
    }
    Vector3 next = goal;
    if (path.corners.Length > 1) {
        while (corner < path.corners.Length - 1 && Vector3.Distance(new Vector3(at.x, 0, at.z), new Vector3(path.corners[corner].x, 0, path.corners[corner].z)) < 1.2f) corner++;
        next = path.corners[Mathf.Min(corner, path.corners.Length - 1)];
        // Swing wide of the corner the way a player does, instead of shaving the pillar the path hugs.
        if (corner < path.corners.Length - 1 && UnityEngine.AI.NavMesh.FindClosestEdge(next, out var edge, filter) && edge.distance < 1.1f)
            next += new Vector3(edge.normal.x, 0f, edge.normal.z).normalized * (1.1f - edge.distance);
    }
    Vector3 d = next - at; d.y = 0f;
    float stamina = RyanAssets.Characters.Client.StaminaController.Stamina;
    float max = Mathf.Max(1f, RyanAssets.Characters.Client.StaminaController.MaxStamina);
    if (WALKAFTERf > 0f && now - started > WALKAFTERf) sprinting = false;
    else if (sprinting && stamina < 1f) sprinting = false;
    else if (!sprinting && stamina >= max * AGAIN / 100f) sprinting = true;
    bool arrived = new Vector2(goal.x - at.x, goal.z - at.z).magnitude < 1.5f || me.IsDead;
    if (arrived) {
        mv.Controls.move = Vector2.zero; mv.Controls.sprint = false;
        System.IO.File.AppendAllText(log, $"{now - started:0.0}s END {(me.IsDead ? "dead" : "arrived")} at {at} after {sidesteps} sidesteps\n");
        UnityEditor.EditorApplication.update -= tick; System.AppDomain.CurrentDomain.SetData(key, null);
        return;
    }
    Transform him = crane();
    float craneGap = him != null ? Vector3.Distance(him.position, at) : -1f;
    if (DROPAT > 0f && !dropped && him != null && craneGap < DROPAT) {
        putDown.Invoke(ctl, null); dropped = true;
        System.IO.File.AppendAllText(log, $"{now - started:0.0}s DROP (R) with Crane {craneGap:0.0} m away\n");
    }
    if (d.sqrMagnitude > 0.01f) {
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        yawField.SetValue(fp, yaw);
        // With the client's cameras switched off (to keep the software renderer from starving the bot of frames) the
        // movement reads the body's own facing, so turn the body too.
        if (Camera.main == null) { var turn = Quaternion.Euler(0f, yaw, 0f); mv.Body.rotation = turn; mv.Body.transform.rotation = turn; }
    }
    // Like a player who bumps a pillar: if barely moving, sidestep for a moment (alternating sides).
    if (now >= stuckCheck) {
        if (stuckCheck > 0f && now >= sidestepUntil && Vector3.Distance(at, lastAt) < 0.4f) { sidestepUntil = now + 0.45f; side = -side; sidesteps++; }
        lastAt = at; stuckCheck = now + 0.6f;
    }
    mv.Controls.move = now < sidestepUntil ? new Vector2(side, 0.25f).normalized : new Vector2(0f, 1f);
    mv.Controls.sprint = sprinting;
    if (now >= nextLog) {
        nextLog = now + 0.5f;
        float speed = me.GetComponent<Rigidbody>() != null ? new Vector2(me.GetComponent<Rigidbody>().linearVelocity.x, me.GetComponent<Rigidbody>().linearVelocity.z).magnitude : -1f;
        System.IO.File.AppendAllText(log, $"{now - started:0.0}s pos {at.x:0.0},{at.y:0.0},{at.z:0.0} speed {speed:0.0} stamina {stamina:0} {(sprinting ? "SPRINT" : "walk")} goal {Vector3.Distance(at, goal):0.0}m crane {craneGap:0.0}m fps {frames / 0.5f:0}{(now < sidestepUntil ? " sidestep" : "")}\n");
        frames = 0;
    }
};
UnityEditor.EditorApplication.update += tick;
System.AppDomain.CurrentDomain.SetData(key, tick);
return "running to " + goal;
