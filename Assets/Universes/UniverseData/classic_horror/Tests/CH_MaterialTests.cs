using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>
    /// The project's URP material validation turns _EMISSION off unless the material's GI flags say it is
    /// emissive, which silently loses a glow after Play or in batchmode. These guard the generated glows.
    /// </summary>
    public sealed class CH_MaterialTests {
        const string Materials = "Assets/Universes/UniverseData/classic_horror/Materials/";

        [TestCase("SearchLanternGlass")]
        [TestCase("ItemEmber")]
        [TestCase("ItemFlame")]
        public void GeneratedGlowMaterialsKeepTheirEmission(string name) {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat");
            Assert.IsNotNull(material, name + ".mat is missing; run Ryan/Classic Horror/Rebuild Item Prefabs.");
            Assert.IsTrue(material.IsKeywordEnabled("_EMISSION"), name + " lost its _EMISSION keyword.");
            Assert.IsTrue((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.RealtimeEmissive) != 0,
                name + " must be flagged RealtimeEmissive or validation strips its emission.");
            Assert.Greater(material.GetColor("_EmissionColor").maxColorComponent, 0f, name + " has a black emission colour.");
        }
    }
}
