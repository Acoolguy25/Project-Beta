// SERVER PROBE "lives": staging only. Tops every investigator up to 9 lives so a staged catch does not end the night.
foreach (var c in RyanAssets.Characters.Shared.LocalCharacter.Characters.Values)
    if (c && RyanAssets.DataService.PlayerData.TryGetPlayerData(c.Owner, out var p)) p.lives.Value = 9;
return "lives 9";
