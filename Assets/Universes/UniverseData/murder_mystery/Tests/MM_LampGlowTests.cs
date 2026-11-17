using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery.Tests {
    /// <summary>
    /// The standing lamp's shade must keep glowing. URP's material validation clears _EMISSION from a material
    /// whose GI flags have no emissive bit or whose emission is black, which happened silently in Play mode
    /// and on Editor stop before the flag was set.
    /// </summary>
    public sealed class MM_LampGlowTests {
        const string GlowMaterialPath = "Assets/Universes/UniverseData/murder_mystery/Materials/MM_LampGlow.mat";

        [Test]
        public void LampGlowMaterialKeepsItsEmission() {
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            Assert.IsNotNull(glow, GlowMaterialPath);
            Assert.IsTrue(glow.IsKeywordEnabled("_EMISSION"), "_EMISSION keyword is off");
            Assert.IsTrue((glow.globalIlluminationFlags & MaterialGlobalIlluminationFlags.RealtimeEmissive) != 0,
                "GI flags need RealtimeEmissive, or URP turns _EMISSION off again");
            Color emission = glow.GetColor("_EmissionColor");
            Assert.Greater(emission.maxColorComponent, 0f, "emission colour is black");
            Assert.IsNotNull(glow.GetTexture("_EmissionMap"), "emission map missing");
        }
    }
}
