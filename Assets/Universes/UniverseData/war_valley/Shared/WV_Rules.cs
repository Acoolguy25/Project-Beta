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
        /// How strongly a commander's colour is mixed into their buildings' authored albedo. Full
        /// strength would replace the Cartoon Military art with flat colour, so ownership reads as a
        /// tint over the model rather than instead of it.
        /// </summary>
        public const float OwnerTintStrength = 0.55f;

        /// <summary>The albedo tint applied to a structure owned by <paramref name="clientId"/>.</summary>
        public static Color GetOwnerTint(int clientId) =>
            Color.Lerp(Color.white, TeamConfig.TeamToColor(GetCommanderColor(clientId)), OwnerTintStrength);

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

        /// <summary>Formats remaining build or production time for the HUD.</summary>
        public static string FormatCountdown(float secondsRemaining) {
            if (secondsRemaining <= 0f)
                return "0s";
            int total = Mathf.CeilToInt(secondsRemaining);
            return total >= 60 ? $"{total / 60}m {total % 60}s" : $"{total}s";
        }

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
