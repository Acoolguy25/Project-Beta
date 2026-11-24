// SERVER PROBE "release": lets Father Crane hunt again.
var m = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Server.CH_Monster>();
m.Resume(); return "released, state " + m.State;
