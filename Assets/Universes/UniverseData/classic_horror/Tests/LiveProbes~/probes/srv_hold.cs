// SERVER PROBE "hold": suspends Father Crane (he stands still and forgets the hunt) until srv_release.cs.
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
m.Suspend(); return "held at " + m.transform.position;
