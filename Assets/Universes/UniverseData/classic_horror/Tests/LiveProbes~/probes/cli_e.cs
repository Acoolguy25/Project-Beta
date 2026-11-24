// CLIENT INPUT: presses E through the controller's own Interact (focus is computed from the camera as for a player).
var ctl = UnityEngine.Object.FindAnyObjectByType<Universes.UniverseData.classic_horror.Client.CH_ClientController>();
ctl.GetType().GetMethod("Interact", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(ctl, null);
return "E";
