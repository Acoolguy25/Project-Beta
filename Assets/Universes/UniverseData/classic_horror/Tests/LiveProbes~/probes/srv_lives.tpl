// SERVER PROBE "lives": staging only. Tops every investigator up to LIVES lives (once, at the start of a bot night, so a bot that cannot dodge Crane still reaches the later acts).
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values)
    if (c && RyanAssets.DataService.PlayerData.TryGetPlayerData(c.Owner, out var p)) p.lives.Value = LIVES;
return "lives LIVES";
