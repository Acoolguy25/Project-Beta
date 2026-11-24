// CLIENT PROBE "tp": puts the local investigator at (PX, PY, PZ) facing yaw YAW.
var bf = System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var stand = new Vector3(PX, PY, PZ);
if (Physics.Raycast(stand + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast", "Water"), QueryTriggerInteraction.Ignore)) stand = hit.point + Vector3.up * 0.05f;
var mv = RyanAssets.Characters.Client.CharacterMovement.Local;
mv.Body.position = stand; mv.Body.linearVelocity = Vector3.zero; mv.transform.position = stand;
var fp = UnityEngine.Object.FindAnyObjectByType<RyanAssets.Cameras.FirstPersonController>(FindObjectsInactive.Include);
fp.GetType().GetField("yaw", bf).SetValue(fp, YAWf);
return "at " + stand;
