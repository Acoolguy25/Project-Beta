using System;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>What an ability is, for the HUD and the server alike: its side, key slot, name and unlocks.</summary>
    public readonly struct HS_AbilityInfo {
        public readonly HS_Ability Ability;
        /// <summary>True for the seekers' side (seekers and found hiders), false for hiders.</summary>
        public readonly bool Seeker;
        /// <summary>Key slot, 0..<see cref="HS_Abilities.SlotsPerSide"/>-1 (key 1, 2, 3).</summary>
        public readonly int Slot;
        public readonly string Name;
        public readonly string Description;
        /// <summary>Theme icon name (Tools/UIIcons/icons.json).</summary>
        public readonly string Icon;
        /// <summary>The <see cref="HS_Retention"/> unlock that grants it.</summary>
        public readonly string UnlockId;
        /// <summary>The unlock that upgrades it (<see cref="HS_AbilityTuning"/>'s upgraded stats).</summary>
        public readonly string UpgradeId;

        public HS_AbilityInfo(HS_Ability ability, bool seeker, int slot, string name, string description, string icon, string unlockId, string upgradeId) {
            Ability = ability;
            Seeker = seeker;
            Slot = slot;
            Name = name;
            Description = description;
            Icon = icon;
            UnlockId = unlockId;
            UpgradeId = upgradeId;
        }

        public bool IsValid => Ability != HS_Ability.None;
    }

    /// <summary>
    /// The six role abilities and the rules both sides agree on: who may use what when, and the maths of
    /// the flashlight cone and movement effects. Numbers live in <see cref="HS_AbilityTuning"/> on the
    /// server runner; the server sends each player their bar in <see cref="HS_AbilityStateBroadcast"/>.
    /// </summary>
    public static class HS_Abilities {
        public const int SlotsPerSide = 3;
        /// <summary>Size of an array indexed by <see cref="HS_Ability"/>.</summary>
        public const int Count = (int)HS_Ability.Flashlight + 1;

        static readonly HS_AbilityInfo[] Table = {
            default,
            new(HS_Ability.Dash, false, 0, "Dash", "A burst of speed to reach a far hiding spot or break away.", "sprint", HS_Retention.UnlockIds.Dash, HS_Retention.UnlockIds.DashPlus),
            new(HS_Ability.Decoy, false, 1, "Decoy", "Throw a noise maker: it sounds like a taunt and shows up on seeker pings and radar.", "campaign", HS_Retention.UnlockIds.Decoy, HS_Retention.UnlockIds.DecoyPlus),
            new(HS_Ability.Blend, false, 2, "Blend In", "Freeze and fade out: seekers only see your shadow, and pings and radar miss you. Press again to move.", "ghost", HS_Retention.UnlockIds.Blend, HS_Retention.UnlockIds.BlendPlus),
            new(HS_Ability.Rush, true, 0, "Rush", "A burst of speed to run a hider down.", "speed", HS_Retention.UnlockIds.Rush, HS_Retention.UnlockIds.RushPlus),
            new(HS_Ability.Radar, true, 1, "Radar", "Sense roughly where hiders are around you. Decoys fool it.", "radio", HS_Retention.UnlockIds.Radar, HS_Retention.UnlockIds.RadarPlus),
            new(HS_Ability.Flashlight, true, 2, "Flashlight", "A blinding flash ahead: hiders in the beam are revealed and slowed, and blending stops.", "bolt", HS_Retention.UnlockIds.Flashlight, HS_Retention.UnlockIds.FlashlightPlus),
        };

        /// <summary>The ability's description, or an invalid one for <see cref="HS_Ability.None"/> and unknown values.</summary>
        public static HS_AbilityInfo Info(HS_Ability ability) => (int)ability > 0 && (int)ability < Table.Length ? Table[(int)ability] : default;

        public static bool IsKnown(HS_Ability ability) => Info(ability).IsValid;

        /// <summary>The ability in <paramref name="slot"/> of a side's bar.</summary>
        public static HS_Ability InSlot(bool seeker, int slot) {
            foreach (HS_AbilityInfo info in Table)
                if (info.IsValid && info.Seeker == seeker && info.Slot == slot) return info.Ability;
            return HS_Ability.None;
        }

        /// <summary>Seekers and found hiders use the seekers' bar.</summary>
        public static bool SeekerSide(HS_Role role) => role is HS_Role.Seeker or HS_Role.Caught;

        /// <summary>Whether <paramref name="role"/> has a bar at all (spectators do not).</summary>
        public static bool HasBar(HS_Role role) => role is HS_Role.Hider or HS_Role.Seeker or HS_Role.Caught;

        /// <summary>
        /// Why the server turns a use down (shown to the player), or null when it is allowed. Hiders may use
        /// theirs while hiding and seeking; the seekers' side only once they are released.
        /// </summary>
        public static string Refusal(HS_Phase phase, HS_Role role, HS_Ability ability, bool unlocked, float readyIn) {
            HS_AbilityInfo info = Info(ability);
            if (!info.IsValid || !HasBar(role) || info.Seeker != SeekerSide(role)) return "That ability is not yours this round.";
            bool open = info.Seeker ? phase == HS_Phase.Seeking : phase is HS_Phase.Hiding or HS_Phase.Seeking;
            if (!open) return info.Seeker ? "Abilities wake up when the seekers are released." : "Abilities only work during a round.";
            if (!unlocked) return $"{info.Name} is locked. Level up to unlock it.";
            if (readyIn > 0f) return $"{info.Name} is recharging.";
            return null;
        }

        /// <summary>Abilities a second press ends early (Blend In: step out of it when a seeker walks up).</summary>
        public static bool CanCancel(HS_Ability ability) => ability == HS_Ability.Blend;

        /// <summary>
        /// Whether a press is the owner ending their running ability early rather than a new use: the
        /// ability can be cancelled, it is on this role's side, and it still has <paramref name="activeLeft"/>
        /// seconds. The cooldown keeps running from the original use.
        /// </summary>
        public static bool Cancels(HS_Role role, HS_Ability ability, float activeLeft) {
            HS_AbilityInfo info = Info(ability);
            return info.IsValid && CanCancel(ability) && HasBar(role) && info.Seeker == SeekerSide(role) && activeLeft > 0f;
        }

        /// <summary>Whether <paramref name="target"/> is inside a beam from <paramref name="origin"/> along <paramref name="forward"/>.</summary>
        public static bool InCone(Vector3 origin, Vector3 forward, Vector3 target, float range, float coneDegrees) {
            Vector3 to = target - origin;
            float distance = to.magnitude;
            if (distance > range) return false;
            if (distance < 1e-4f) return true;
            if (forward.sqrMagnitude < 1e-8f) return false;
            return Vector3.Angle(forward, to) <= coneDegrees * 0.5f;
        }

        /// <summary>
        /// A safe aim direction from a client: normalised, or the fallback when it is zero or not a number.
        /// </summary>
        public static Vector3 SafeDirection(Vector3 requested, Vector3 fallback) {
            if (float.IsNaN(requested.x) || float.IsNaN(requested.y) || float.IsNaN(requested.z) || requested.sqrMagnitude < 1e-6f)
                requested = fallback;
            return requested.sqrMagnitude < 1e-6f ? Vector3.forward : requested.normalized;
        }
    }

    /// <summary>One ability's numbers. What <see cref="power"/> means depends on the ability (see <see cref="HS_AbilityTuning"/>).</summary>
    [Serializable]
    public sealed class HS_AbilityStats {
        [Tooltip("Seconds between two uses.")]
        [Min(1f)] public float cooldown;
        [Tooltip("Seconds the effect lasts (0 = instant).")]
        [Min(0f)] public float duration;
        [Tooltip("Speed multiplier, throw distance, radius or beam range (see the ability).")]
        [Min(0f)] public float power;

        public HS_AbilityStats() { }

        public HS_AbilityStats(float cooldown, float duration, float power) {
            this.cooldown = cooldown;
            this.duration = duration;
            this.power = power;
        }

        public void Validate() {
            cooldown = Mathf.Max(1f, float.IsNaN(cooldown) ? 1f : cooldown);
            duration = Mathf.Max(0f, float.IsNaN(duration) ? 0f : duration);
            power = Mathf.Max(0f, float.IsNaN(power) ? 0f : power);
        }
    }

    /// <summary>An ability's stats before and after its upgrade unlock.</summary>
    [Serializable]
    public sealed class HS_AbilitySpec {
        public HS_AbilityStats basic = new();
        public HS_AbilityStats upgraded = new();

        public HS_AbilitySpec() { }

        public HS_AbilitySpec(HS_AbilityStats basic, HS_AbilityStats upgraded) {
            this.basic = basic;
            this.upgraded = upgraded;
        }

        public HS_AbilityStats Get(bool isUpgraded) => isUpgraded ? upgraded : basic;
    }

    /// <summary>
    /// Every ability number, serialized on the server runner. Distances are world units (the robot is
    /// about 5.6 tall). Defaults: an ability is worth using about two or three times a round.
    /// </summary>
    [Serializable]
    public sealed class HS_AbilityTuning {
        [Tooltip("Hiders. Power = speed multiplier while it lasts (at least 1).")]
        public HS_AbilitySpec dash = new(new HS_AbilityStats(18f, 2.5f, 1.6f), new HS_AbilityStats(15f, 3.5f, 1.7f));
        [Tooltip("Hiders. Power = how far it is thrown; duration = how long it counts on pings, radar and for fooling a seeker.")]
        public HS_AbilitySpec decoy = new(new HS_AbilityStats(28f, 20f, 22f), new HS_AbilityStats(22f, 30f, 32f));
        [Tooltip("Hiders. Duration = how long you are faded and rooted. Power unused.")]
        public HS_AbilitySpec blend = new(new HS_AbilityStats(35f, 7f, 0f), new HS_AbilityStats(28f, 10f, 0f));
        [Tooltip("Seekers. Power = speed multiplier while it lasts (at least 1).")]
        public HS_AbilitySpec rush = new(new HS_AbilityStats(16f, 2.5f, 1.5f), new HS_AbilityStats(13f, 3.5f, 1.6f));
        [Tooltip("Seekers. Power = radius; duration = how long the markers stay up.")]
        public HS_AbilitySpec radar = new(new HS_AbilityStats(30f, 5f, 45f), new HS_AbilityStats(24f, 6f, 65f));
        [Tooltip("Seekers. Power = beam range; duration = how long a hit hider stays revealed and slowed.")]
        public HS_AbilitySpec flashlight = new(new HS_AbilityStats(25f, 3f, 32f), new HS_AbilityStats(20f, 4f, 44f));

        [Header("Shared")]
        [Tooltip("Full width of the flashlight beam (degrees).")]
        [Range(5f, 120f)] public float flashlightConeDegrees = 50f;
        [Tooltip("Speed multiplier of a hider the flashlight dazzled.")]
        [Range(0.1f, 1f)] public float dazzledSpeedScale = 0.45f;
        [Tooltip("Radar positions are blurred this far (world units) so they say 'over there', not 'right here'.")]
        [Min(0f)] public float radarScatter = 5f;
        [Tooltip("A seeker this close to a live decoy has been fooled by it (pays the hider once per decoy).")]
        [Min(0f)] public float decoyFoolRange = 9f;
        [Tooltip("Cooldown multiplier for players who own Quick Recovery.")]
        [Range(0.3f, 1f)] public float quickRecoveryCooldownScale = 0.8f;

        public HS_AbilitySpec Spec(HS_Ability ability) => ability switch {
            HS_Ability.Dash => dash,
            HS_Ability.Decoy => decoy,
            HS_Ability.Blend => blend,
            HS_Ability.Rush => rush,
            HS_Ability.Radar => radar,
            HS_Ability.Flashlight => flashlight,
            _ => null,
        };

        public HS_AbilityStats Stats(HS_Ability ability, bool upgraded) =>
            Spec(ability)?.Get(upgraded) ?? throw new ArgumentOutOfRangeException(nameof(ability), ability, "Not an ability.");

        /// <summary>Seconds between two uses for this player.</summary>
        public float Cooldown(HS_Ability ability, bool upgraded, bool quickRecovery) =>
            Stats(ability, upgraded).cooldown * (quickRecovery ? quickRecoveryCooldownScale : 1f);

        /// <summary>Clamps every value into a sane range (call at the authority boundary, e.g. the runner's Awake).</summary>
        public void Validate() {
            dash ??= new HS_AbilitySpec();
            decoy ??= new HS_AbilitySpec();
            blend ??= new HS_AbilitySpec();
            rush ??= new HS_AbilitySpec();
            radar ??= new HS_AbilitySpec();
            flashlight ??= new HS_AbilitySpec();
            foreach (HS_AbilitySpec spec in new[] { dash, decoy, blend, rush, radar, flashlight }) {
                spec.basic ??= new HS_AbilityStats();
                spec.upgraded ??= new HS_AbilityStats();
                spec.basic.Validate();
                spec.upgraded.Validate();
            }
            // A speed burst never slows: a power below 1 (or a missing spec read as zeros) would root the player.
            foreach (HS_AbilitySpec speed in new[] { dash, rush }) {
                speed.basic.power = Mathf.Max(1f, speed.basic.power);
                speed.upgraded.power = Mathf.Max(1f, speed.upgraded.power);
            }
            flashlightConeDegrees = Mathf.Clamp(flashlightConeDegrees, 5f, 120f);
            dazzledSpeedScale = Mathf.Clamp(dazzledSpeedScale, 0.1f, 1f);
            radarScatter = Mathf.Max(0f, radarScatter);
            decoyFoolRange = Mathf.Max(0f, decoyFoolRange);
            quickRecoveryCooldownScale = Mathf.Clamp(quickRecoveryCooldownScale, 0.3f, 1f);
        }
    }
}
