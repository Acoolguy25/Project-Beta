using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>Course dressing pieces, one mesh each per tier (see OB_EnvironmentKit).</summary>
    public enum OB_DressingProp : byte { Keel, Trim, Pillar, PillarCap, Arch, Banner, Lamp, Rope, Pennant }

    /// <summary>Scenery kinds placed by <see cref="OB_ScenePlanner"/>.</summary>
    public enum OB_SceneryKind : byte { Rock, Island, Landmark, Cloud, Flock }

    /// <summary>One planned piece of scenery or dressing: what, which variant, in which tier's style, and where.</summary>
    public struct OB_Placement {
        /// <summary>An <see cref="OB_DressingProp"/> or <see cref="OB_SceneryKind"/>, depending on the planner.</summary>
        public byte kind;
        /// <summary>Mesh or prefab variant (for landmarks: the index into the tier's landmark list).</summary>
        public int variant;
        public OB_Tier tier;
        public Matrix4x4 matrix;
        /// <summary>World box the planner kept clear of the course.</summary>
        public Bounds bounds;
    }
}
