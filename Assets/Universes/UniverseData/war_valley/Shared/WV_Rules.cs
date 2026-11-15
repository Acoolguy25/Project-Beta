using RyanAssets.Shared.WorldUI;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>What a War Valley build slot produces once its construction finishes.</summary>
    public enum WV_UnitKind : byte {
        None = 0,
        Infantry = 1,
        Tank = 2,
        APC = 3,
        Artillery = 4,
        Chopper = 5,
        Jet = 6,
        Bomber = 7,
        UAV = 8
    }

    /// <summary>
    /// A kind of War Valley foot soldier. Wave troops and the troops a commander trains are the same
    /// character with a different weapon and build (see <see cref="WV_TroopCatalog"/>), so one enum
    /// describes both. Travels on the wire inside a production item: append, never renumber.
    /// </summary>
    public enum WV_TroopKind : byte {
        Knife = 0,
        Gunner = 1,
        SkinnyLegend = 2,
        Punk = 3,
        Shrimp = 4,
        Speedy = 5
    }

    /// <summary>
    /// Why the server would not queue something at a production building.
    /// <para>
    /// Queueing is refused for several perfectly ordinary reasons, and a button that silently does
    /// nothing is indistinguishable from a broken one, so the reason comes back to the sender and
    /// the HUD says which it was. The numbering is unchanged from when this covered troops alone.
    /// </para>
    /// </summary>
    public enum WV_TroopRefusal : byte {
        None = 0,
        NotEnoughFunds = 1,
        /// <summary>The payer already fields, or has queued, as many of this kind as <see cref="WV_Limits"/> allows.</summary>
        SquadFull = 2,
        QueueFull = 3,
        NotOperational = 4,
        Unavailable = 5,
        /// <summary>The item waits on research the commander's side has not finished.</summary>
        Locked = 6
    }

    /// <summary>
    /// Why the server would not start or cancel a research project. Travels on the wire in
    /// <see cref="WV_ResearchResult"/>, so values may be appended but never renumbered.
    /// </summary>
    public enum WV_ResearchRefusal : byte {
        None = 0,
        NotEnoughFunds = 1,
        AlreadyResearched = 2,
        AlreadyResearching = 3,
        /// <summary>No finished research station on the commander's side to run the project.</summary>
        NoResearchStation = 4,
        MissingPrerequisite = 5,
        Unavailable = 6
    }

    /// <summary>The order a selected group is currently carrying out.</summary>
    public enum WV_OrderType : byte {
        Stop = 0,
        Move = 1,
        AttackMove = 2,
        Attack = 3,
        HoldPosition = 4,
        /// <summary>Stops movement and weapons until another order is issued.</summary>
        Freeze = 5
    }

    /// <summary>
    /// Tuning and footprint math shared by the authoring pass, the client preview, and the
    /// authoritative server. Anything that both a generated prefab and runtime validation must
    /// agree on belongs here rather than being duplicated per structure.
    /// </summary>
    public static class WV_Rules {
        /// <summary>
        /// Structure ids the rules refer to by name: the research lock and the connected-shield
        /// placement rule both key on the shield generator.
        /// </summary>
        public const string ShieldGeneratorId = "wv_shield_generator";
        public const string GateId = "wv_gate";

        /// <summary>
        /// Funds for a mine, barracks, watchtower and a starting squad, with room left for a second
        /// income building or an early defence, so the opener is a choice rather than a scrape.
        /// </summary>
        public const long StartingFunds = 2400;

        /// <summary>
        /// Leaderboard column the player list shows a commander's balance in. The shared leaderboard
        /// is keyed by header name, so the server and the column lookup must use this one constant.
        /// </summary>
        public const string CoinsLeaderboard = "Coins";

        /// <summary>Income buildings pay out on this cadence so the HUD can show a stable per-minute rate.</summary>
        public const float IncomeTickSeconds = 5f;

        /// <summary>Recovery income, even when a commander has lost every income building.</summary>
        public const int CommanderIncomePerTick = 20;

        /// <summary>A queued unit is refunded in full if its building dies before the unit pops.</summary>
        public const float ProductionRefundFraction = 1f;

        /// <summary>Construction runs at this fraction of the structure's authored Duration on the last player standing.</summary>
        public const float MinConstructionSeconds = 1f;

        /// <summary>A structure is only worth its remaining build progress while it is still a site.</summary>
        public const float UnderConstructionHealthFraction = 0.25f;

        /// <summary>Selection and order raycasts ignore everything a unit could never stand on.</summary>
        public static int OrderGroundMask =>
            ~LayerMask.GetMask("Character", "LocalCharacter", "Ignore Raycast", "UI");

        /// <summary>
        /// The NavMesh agent type War Valley's terrain is baked for. Ground units and the breach links
        /// cut through walls must both use it or a unit will never path across the valley.
        /// </summary>
        public const int NavMeshAgentTypeId = -902729914;

        /// <summary>
        /// The 4-unit build grid already used by <c>StructurePlacement</c>. War Valley sizes every
        /// imported military model against this so a hangar and a fence occupy whole, adjacent cells
        /// instead of each model's arbitrary authored scale.
        /// </summary>
        public const float GridSize = 4f;

        /// <summary>
        /// The uniform scale that makes <paramref name="sourceBounds"/> span exactly
        /// <paramref name="footprintCells"/> grid cells on its widest horizontal axis.
        /// Models larger than their slot shrink; models smaller than it expand.
        /// </summary>
        public static float GetGridFitScale(Bounds sourceBounds, int footprintCells) {
            float targetSpan = Mathf.Max(1, footprintCells) * GridSize;
            float widestAxis = Mathf.Max(sourceBounds.size.x, sourceBounds.size.z);
            // A degenerate import would otherwise divide by zero and blow the prefab up to infinity.
            return widestAxis <= 0.0001f ? 1f : targetSpan / widestAxis;
        }

        /// <summary>
        /// Height of the construction scaffold for a structure of this footprint. The scaffold has to
        /// enclose the finished building, so it scales with the slot rather than with the model.
        /// </summary>
        public static float GetScaffoldHeight(int footprintCells) =>
            Mathf.Max(1, footprintCells) * GridSize * 0.75f;

        /// <summary>
        /// The colours commanders are identified by. Every player fights on the same real team, so
        /// these are handed out as <see cref="TeamConfig.displayTeam"/> - the existing presentation
        /// half of the team setting - rather than as a second parallel colour setting. Blue and Red
        /// are deliberately absent: they are the match's real teams and would read as "friendly" or
        /// "hostile" instead of as an owner.
        /// </summary>
        public static readonly TeamColor[] CommanderColors = {
            TeamColor.Cyan, TeamColor.Orange, TeamColor.Lime, TeamColor.Purple,
            TeamColor.Pink, TeamColor.Yellow, TeamColor.Teal, TeamColor.Green
        };

        /// <summary>
        /// The colour a commander's structures and units carry. Derived from the client id alone so
        /// the server, the owner, and every other client agree without replicating a lookup table.
        /// </summary>
        public static TeamColor GetCommanderColor(int clientId) {
            if (clientId == WV_Owned.NoOwner)
                return TeamColor.Grey;
            // Client ids are non-negative in practice; guard anyway so a sentinel never indexes back
            // off the front of the palette.
            int index = Mathf.Abs(clientId) % CommanderColors.Length;
            return CommanderColors[index];
        }

        /// <summary>
        /// The commander's colour at full strength, for UI drawn over the world rather than for a
        /// model's albedo. A build timer's owner strip should read as the commander's colour outright,
        /// not as the washed-out tint that keeps the Cartoon Military art visible underneath it.
        /// </summary>
        public static Color GetCommanderUIColor(int clientId) =>
            TeamConfig.TeamToColor(GetCommanderColor(clientId));

        /// <summary>
        /// The health fraction at which a structure starts visibly corroding. Above this its colours
        /// stay clean; below it they are progressively eaten by brown rust, so damage reads from the
        /// first real hit and a building about to go up is unmistakable from across the valley.
        /// </summary>
        public const float CorrosionHealthFraction = 0.9f;

        /// <summary>Rusted-through albedo a structure reaches at the instant it dies.</summary>
        public static readonly Color CorrodedTint = new(0.36f, 0.21f, 0.1f);

        /// <summary>
        /// 0 while a structure is above <see cref="CorrosionHealthFraction"/> of its health, rising
        /// to 1 as it reaches zero: how far every colour on it has been pulled toward rust. Eased so
        /// the first chunk of damage already shows, rather than the rust waiting for the last third.
        /// </summary>
        public static float GetCorrosion(float healthFraction) {
            if (healthFraction >= CorrosionHealthFraction)
                return 0f;
            float damage = 1f - Mathf.Clamp01(healthFraction / CorrosionHealthFraction);
            return Mathf.Pow(damage, 0.7f);
        }

        /// <summary>
        /// How strongly a building's authored colours give way to its commander's colour scheme.
        /// Buildings come from several art packs with clashing colours; pulling every colour onto
        /// one ramp built from the commander's colour makes a base read as one player's kit.
        /// </summary>
        public const float StructurePaletteStrength = 0.6f;

        /// <summary>
        /// The same for walls, fences and gates, kept light. A wall is a long plain surface, and a
        /// strong or flat team colour across it looks like a painted plank; a hint of the owner's
        /// hue over the authored material reads as theirs without that.
        /// </summary>
        public const float WallPaletteStrength = 0.28f;

        /// <summary>
        /// <paramref name="authored"/> moved onto <paramref name="clientId"/>'s colour scheme: a ramp
        /// from a deep shade of their colour through the colour itself to a pale tint of it. The
        /// authored brightness picks the step, so dark trim stays dark, panels stay light, and the
        /// model keeps its shape while taking the commander's hue.
        /// </summary>
        public static Color GetStructurePaletteColor(Color authored, int clientId, float strength) {
            Color hue = GetCommanderUIColor(clientId);
            Color.RGBToHSV(hue, out float h, out float s, out float v);
            Color shade = Color.HSVToRGB(h, s * 0.55f, 0.22f);
            Color mid = Color.HSVToRGB(h, s * 0.65f, Mathf.Max(0.55f, v * 0.8f));
            Color tint = Color.HSVToRGB(h, s * 0.18f, 0.92f);

            float luminance = Mathf.Clamp01(authored.r * 0.299f + authored.g * 0.587f + authored.b * 0.114f);
            Color ramp = luminance < 0.5f
                ? Color.Lerp(shade, mid, luminance * 2f)
                : Color.Lerp(mid, tint, (luminance - 0.5f) * 2f);
            Color result = Color.Lerp(authored, ramp, strength);
            result.a = authored.a;
            return result;
        }

        /// <summary>
        /// The commander colour as painted onto a building's main body: their colour at full hue,
        /// a touch less saturated and bright than the UI colour so it reads as paint, not neon.
        /// </summary>
        public static Color GetCommanderPaintColor(int clientId) {
            Color.RGBToHSV(GetCommanderUIColor(clientId), out float h, out float s, out float v);
            return Color.HSVToRGB(h, s * 0.85f, v * 0.88f);
        }

        /// <summary>A finished structure starts repairing itself once it has gone this long without a hit.</summary>
        public const float StructureRepairDelaySeconds = 10f;

        /// <summary>Share of its maximum health an undisturbed structure regains per second.</summary>
        public const float StructureRepairPerSecondFraction = 0.015f;

        /// <summary>How long the hit flash on a damaged structure lasts.</summary>
        public const float HitFlashSeconds = 0.18f;

        /// <summary>Colour a structure flashes toward the instant it takes a hit.</summary>
        public static readonly Color HitFlashTint = new(1f, 0.55f, 0.35f);

        /// <summary>Spacing between units fanned out around a single group order.</summary>
        public const float GroupFormationSpacing = 3.2f;

        /// <summary>
        /// Where one member of a group order should actually stand. Index 0 takes the clicked point and
        /// the rest ring outward, so a twelve-unit order spreads into a formation instead of collapsing
        /// into a single stack that shoves itself apart.
        /// </summary>
        public static Vector3 GetGroupDestination(Vector3 center, int index, int total) {
            if (total <= 1 || index <= 0)
                return center;

            int ring = 1;
            int consumed = 1;
            while (true) {
                // Ring capacity grows with circumference, with a floor so the first ring is not
                // so sparse that a squad of three looks scattered.
                int capacity = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * ring));
                if (index < consumed + capacity) {
                    float angle = (index - consumed) / (float)capacity * Mathf.PI * 2f;
                    return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                        * (ring * GroupFormationSpacing);
                }
                consumed += capacity;
                ring++;
            }
        }

        /// <summary>
        /// Formats remaining build or production time for the HUD. Forwards to the shared world-timer
        /// formatter so the HUD's production queue and the bar floating over the site itself round and
        /// abbreviate the same countdown identically.
        /// </summary>
        public static string FormatCountdown(float secondsRemaining) =>
            WorldTimerBar.FormatCountdown(secondsRemaining);

        public static string GetUnitDisplayName(WV_UnitKind kind) => kind switch {
            WV_UnitKind.Infantry => "Infantry",
            WV_UnitKind.Tank => "Tank",
            WV_UnitKind.APC => "APC",
            WV_UnitKind.Artillery => "Artillery",
            WV_UnitKind.Chopper => "Chopper",
            WV_UnitKind.Jet => "Jet",
            WV_UnitKind.Bomber => "Bomber",
            WV_UnitKind.UAV => "UAV",
            _ => "Unit"
        };

        // --- Foot soldiers ----------------------------------------------------

        /// <summary>
        /// How far a troop holding a gun will engage from. Deliberately far short of the pistol's own
        /// 60m trace: a rifleman that opened fire the moment a target crossed the horizon would
        /// never close on an objective, and the shot would spend most of its life blocked by terrain.
        /// </summary>
        public const float GunnerEngageRange = 14f;

        /// <summary>
        /// The distance a gunner tries to hold. Anything that closes inside this is too near to
        /// shoot comfortably, so the troop gives ground instead of standing there being stabbed.
        /// </summary>
        public const float GunnerStandoffRange = 5f;

        /// <summary>
        /// How often a gunner offers to pull the trigger. The weapon's own fire rate, clip, and
        /// reload remain the real limit; this only has to be short enough not to add a second one.
        /// </summary>
        public const float GunnerAttackInterval = 0.35f;

        /// <summary>Funds a commander is charged to field one troop.</summary>
        public static int GetTroopCost(WV_TroopKind kind) => WV_TroopCatalog.Get(kind).Cost;

        public static string GetTroopDisplayName(WV_TroopKind kind) => WV_TroopCatalog.Get(kind).DisplayName;

        /// <summary>
        /// "Player0's Skinny Legend": the name a commander's troop or vehicle carries over its head,
        /// so on a shared field everyone can tell whose army is whose.
        /// </summary>
        public static string FormatOwnedName(string ownerName, string baseName) =>
            string.IsNullOrWhiteSpace(ownerName) ? baseName : $"{ownerName.Trim()}'s {baseName}";

        /// <summary>
        /// <see cref="FormatOwnedName"/> for a commander by client id, or the bare name when the
        /// commander is unknown or has left.
        /// </summary>
        public static string GetOwnedName(int ownerClientId, string baseName) =>
            ownerClientId != WV_Owned.NoOwner
            && RyanAssets.DataService.PlayerData.TryGetPlayerData(ownerClientId, out RyanAssets.DataService.PlayerData player)
                ? FormatOwnedName(player.GetPlayerName(), baseName)
                : baseName;

        /// <summary>
        /// Seconds a barracks spends training one troop. Foot soldiers are the cheapest and fastest
        /// thing on the field, so these sit well under any vehicle's build time - but they are no
        /// longer instant, because a queue is what makes them cost tempo as well as funds.
        /// </summary>
        public static float GetTroopBuildSeconds(WV_TroopKind kind) => WV_TroopCatalog.Get(kind).TrainSeconds;

        /// <summary>
        /// The loadout a troop carries, recovered from its display name. A troop's kind is not
        /// replicated as a field; the server names the character from it, so a client reads it back
        /// this way - to price a sale, for instance. Accepts both a wave enemy's bare name and a
        /// commander's troop named by <see cref="FormatOwnedName"/>.
        /// </summary>
        public static bool TryGetTroopKind(string displayName, out WV_TroopKind kind) {
            if (!string.IsNullOrEmpty(displayName)) {
                foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                    if (displayName == profile.DisplayName
                        || displayName.EndsWith("'s " + profile.DisplayName, System.StringComparison.Ordinal)) {
                        kind = profile.Kind;
                        return true;
                    }
                }
            }
            kind = WV_TroopKind.Knife;
            return false;
        }

        /// <summary>One line describing how this troop fights, for the production card.</summary>
        public static string GetTroopDescription(WV_TroopKind kind) => WV_TroopCatalog.Get(kind).Description;

        /// <summary>What the HUD tells the commander when an order to build is refused.</summary>
        public static string GetProductionRefusalMessage(WV_TroopRefusal refusal, WV_ProductionItem item) =>
            refusal switch {
                WV_TroopRefusal.NotEnoughFunds => $"Not enough funds for a {item.DisplayName}",
                WV_TroopRefusal.SquadFull => WV_Limits.GetLimitMessage(WV_Limits.GetCategory(item)),
                WV_TroopRefusal.QueueFull => "Build queue is full",
                WV_TroopRefusal.NotOperational => "That building is not finished",
                WV_TroopRefusal.Locked =>
                    $"Research {WV_TechTree.GetDisplayName(WV_TechTree.GetRequirement(item))} to build a {item.DisplayName}",
                _ => $"Cannot build a {item.DisplayName} here"
            };

        /// <summary>What the HUD tells the commander when research is refused.</summary>
        public static string GetResearchRefusalMessage(WV_ResearchRefusal refusal, WV_Tech tech) {
            string name = WV_TechTree.GetDisplayName(tech);
            return refusal switch {
                WV_ResearchRefusal.NotEnoughFunds => $"Not enough funds to research {name}",
                WV_ResearchRefusal.AlreadyResearched => $"{name} is already researched",
                WV_ResearchRefusal.AlreadyResearching => $"{name} is already being researched",
                WV_ResearchRefusal.NoResearchStation => "Build a research station of your own to research technology",
                WV_ResearchRefusal.MissingPrerequisite => $"{name} needs earlier research first",
                _ => $"Cannot research {name}"
            };
        }

        // --- Penalties and refunds ---------------------------------------------

        /// <summary>Share of a commander's funds lost each time their own character dies.</summary>
        public const float DeathPenaltyFraction = 0.08f;

        /// <summary>
        /// The least a death costs, so a commander sitting on a small balance still feels it. Never
        /// more than they actually hold: a penalty cannot push a balance negative.
        /// </summary>
        public const long DeathPenaltyMinimum = 25;

        /// <summary>Funds taken from a commander holding <paramref name="funds"/> when they die.</summary>
        public static long GetDeathPenalty(long funds) {
            if (funds <= 0)
                return 0;
            long proportional = (long)System.Math.Round(funds * (double)DeathPenaltyFraction);
            return System.Math.Min(funds, System.Math.Max(DeathPenaltyMinimum, proportional));
        }

        /// <summary>
        /// Share of the price a sale hands back for something in perfect condition - a building
        /// demolished, a unit or troop scrapped. The rest is the cost of changing one's mind.
        /// </summary>
        public const float SellRefundFraction = 0.7f;

        /// <summary>
        /// Share of that refund kept however badly damaged the thing is. Damage only trims the value
        /// down to this floor rather than to nothing, because a building at 1 health still works as
        /// well as a pristine one; it is merely closer to being lost.
        /// </summary>
        public const float SellDamagedValueFloor = 0.5f;

        /// <summary>
        /// What selling returns: <see cref="SellRefundFraction"/> of <paramref name="cost"/>, falling
        /// linearly with <paramref name="condition"/> (health against what it should have) to
        /// <see cref="SellDamagedValueFloor"/> of that at zero. At the defaults a sale refunds 50% at
        /// full health and 25% at the last hit point. Buildings, vehicles, aircraft, and troops all
        /// use this one rule, priced from what they cost to build or train.
        /// </summary>
        public static long GetSellRefund(long cost, float condition) {
            if (cost <= 0)
                return 0;
            float value = Mathf.Lerp(SellDamagedValueFloor, 1f, Mathf.Clamp01(condition));
            return (long)System.Math.Round(cost * (double)(SellRefundFraction * value));
        }

        /// <summary><see cref="GetSellRefund(long, float)"/> for a structure's unsigned build price.</summary>
        public static long GetSellRefund(ulong cost, float condition) =>
            GetSellRefund((long)System.Math.Min(cost, (ulong)long.MaxValue), condition);

        /// <summary>How far from its barracks spawn point a newly trained troop is placed.</summary>
        public const float TroopSpawnRadius = 5f;

        /// <summary>Aircraft ignore the NavMesh and hold this altitude above their ground target.</summary>
        public static float GetCruiseAltitude(WV_UnitKind kind) => kind switch {
            WV_UnitKind.Chopper => 6f,
            WV_UnitKind.UAV => 8f,
            WV_UnitKind.Jet => 10f,
            WV_UnitKind.Bomber => 9f,
            _ => 0f
        };

        public static bool IsAircraft(WV_UnitKind kind) =>
            kind is WV_UnitKind.Chopper or WV_UnitKind.Jet or WV_UnitKind.Bomber or WV_UnitKind.UAV;
    }
}
