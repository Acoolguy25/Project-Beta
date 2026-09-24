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
    /// The weapon a War Valley foot soldier carries. Wave troops and the troops a commander trains
    /// are the same character with a different tool in their hands, so one enum describes both.
    /// </summary>
    public enum WV_TroopKind : byte {
        Knife = 0,
        Gunner = 1
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
        HoldPosition = 4
    }

    /// <summary>
    /// Tuning and footprint math shared by the authoring pass, the client preview, and the
    /// authoritative server. Anything that both a generated prefab and runtime validation must
    /// agree on belongs here rather than being duplicated per structure.
    /// </summary>
    public static class WV_Rules {
        /// <summary>Funds every player starts a round with, enough for one mineshaft plus a barracks.</summary>
        public const long StartingFunds = 900;

        /// <summary>
        /// Leaderboard column the player list shows a commander's balance in. The shared leaderboard
        /// is keyed by header name, so the server and the column lookup must use this one constant.
        /// </summary>
        public const string CoinsLeaderboard = "Coins";

        /// <summary>Income buildings pay out on this cadence so the HUD can show a stable per-minute rate.</summary>
        public const float IncomeTickSeconds = 5f;

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
        /// The health fraction at which a structure starts visibly failing. Above this its colours - the
        /// commander's paint and the original art - stay clean; below it they are progressively eaten
        /// by corrosion, so a
        /// building about to go up is readable as such from across the valley.
        /// </summary>
        public const float CorrosionHealthFraction = 0.3f;

        /// <summary>Rusted-through albedo a structure reaches at the instant it dies.</summary>
        public static readonly Color CorrodedTint = new(0.29f, 0.17f, 0.09f);

        /// <summary>
        /// 0 while a structure is above <see cref="CorrosionHealthFraction"/> of its health, rising
        /// to 1 as it reaches zero: how far every colour on it has been pulled toward rust.
        /// </summary>
        public static float GetCorrosion(float healthFraction) =>
            healthFraction >= CorrosionHealthFraction
                ? 0f
                : 1f - Mathf.Clamp01(healthFraction / CorrosionHealthFraction);

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
        public const float GunnerEngageRange = 24f;

        /// <summary>
        /// The distance a gunner tries to hold. Anything that closes inside this is too near to
        /// shoot comfortably, so the troop gives ground instead of standing there being stabbed.
        /// </summary>
        public const float GunnerStandoffRange = 11f;

        /// <summary>
        /// How often a gunner offers to pull the trigger. The weapon's own fire rate, clip, and
        /// reload remain the real limit; this only has to be short enough not to add a second one.
        /// </summary>
        public const float GunnerAttackInterval = 0.35f;

        /// <summary>Funds a commander is charged to field one troop.</summary>
        public static int GetTroopCost(WV_TroopKind kind) => kind switch {
            WV_TroopKind.Gunner => 150,
            _ => 80
        };

        public static string GetTroopDisplayName(WV_TroopKind kind) => kind switch {
            WV_TroopKind.Gunner => "Gunner",
            _ => "Knifeman"
        };

        /// <summary>
        /// Seconds a barracks spends training one troop. Foot soldiers are the cheapest and fastest
        /// thing on the field, so these sit well under any vehicle's build time - but they are no
        /// longer instant, because a queue is what makes them cost tempo as well as funds.
        /// </summary>
        public static float GetTroopBuildSeconds(WV_TroopKind kind) => kind switch {
            WV_TroopKind.Gunner => 10f,
            _ => 6f
        };

        /// <summary>One line describing how this troop fights, for the production card.</summary>
        public static string GetTroopDescription(WV_TroopKind kind) => kind switch {
            WV_TroopKind.Gunner => "Rifleman. Holds range and shoots.",
            _ => "Melee rusher. Cheap and fast."
        };

        /// <summary>What the HUD tells the commander when an order to build is refused.</summary>
        public static string GetProductionRefusalMessage(WV_TroopRefusal refusal, WV_ProductionItem item) =>
            refusal switch {
                WV_TroopRefusal.NotEnoughFunds => $"Not enough funds for a {item.DisplayName}",
                WV_TroopRefusal.SquadFull => $"Squad is full ({MaxTroopsPerCommander} troops)",
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
        public const float DeathPenaltyFraction = 0.15f;

        /// <summary>
        /// The least a death costs, so a commander sitting on a small balance still feels it. Never
        /// more than they actually hold: a penalty cannot push a balance negative.
        /// </summary>
        public const long DeathPenaltyMinimum = 50;

        /// <summary>Funds taken from a commander holding <paramref name="funds"/> when they die.</summary>
        public static long GetDeathPenalty(long funds) {
            if (funds <= 0)
                return 0;
            long proportional = (long)System.Math.Round(funds * (double)DeathPenaltyFraction);
            return System.Math.Min(funds, System.Math.Max(DeathPenaltyMinimum, proportional));
        }

        /// <summary>
        /// Share of the build price handed back when a commander demolishes their own structure.
        /// A finished building returns half; a site still under construction returns more, because
        /// tearing down a misplaced foundation should not cost as much as scrapping a working base.
        /// </summary>
        public const float DemolishRefundFraction = 0.5f;
        public const float DemolishSiteRefundFraction = 0.75f;

        /// <summary>
        /// What demolishing returns, scaled down by the damage the structure has taken:
        /// <paramref name="integrity"/> is its health against what it should have (see
        /// <c>WV_Constructable.Integrity</c>), so a building half shot away refunds half as much and
        /// scrapping a base that is about to fall anyway recovers almost nothing.
        /// </summary>
        public static long GetDemolishRefund(ulong cost, bool operational, float integrity) {
            float fraction = (operational ? DemolishRefundFraction : DemolishSiteRefundFraction)
                * Mathf.Clamp01(integrity);
            return (long)System.Math.Round(System.Math.Min(cost, (ulong)long.MaxValue) * (double)fraction);
        }

        /// <summary>Upper bound on the troops one commander can have alive at once.</summary>
        public const int MaxTroopsPerCommander = 12;

        /// <summary>How far from its barracks spawn point a newly trained troop is placed.</summary>
        public const float TroopSpawnRadius = 5f;

        /// <summary>Aircraft ignore the NavMesh and hold this altitude above their ground target.</summary>
        public static float GetCruiseAltitude(WV_UnitKind kind) => kind switch {
            WV_UnitKind.Chopper => 14f,
            WV_UnitKind.UAV => 26f,
            WV_UnitKind.Jet => 34f,
            WV_UnitKind.Bomber => 30f,
            _ => 0f
        };

        public static bool IsAircraft(WV_UnitKind kind) =>
            kind is WV_UnitKind.Chopper or WV_UnitKind.Jet or WV_UnitKind.Bomber or WV_UnitKind.UAV;
    }
}
