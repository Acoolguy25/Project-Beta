using NUnit.Framework;
using UnityEditor;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    /// <summary>Authored assets and colour rules the board's look depends on.</summary>
    public class DI_PresentationTests {
        const string BaseMaterialPath = "Assets/Universes/UniverseData/dot_invaders/Client/Prefabs/DI_Base.mat";

        static float Luminance(Color color) => 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;

        [Test]
        public void BaseMaterialKeepsEmissionOn() {
            // Bases, troops, marks and the border are coloured through _EmissionColor in property blocks;
            // URP Lit ignores it without the keyword, and the board renders flat with no bloom.
            var material = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            Assert.That(material, Is.Not.Null, BaseMaterialPath);
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True, "DI_Base.mat lost _EMISSION; run Ryan/Dot Invaders/Rebuild HUD and Board.");
            // URP strips _EMISSION on its next validation unless a GI emissive flag is set.
            Assert.That(material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive, Is.Not.EqualTo(MaterialGlobalIlluminationFlags.None));
            Assert.That(material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack, Is.EqualTo(MaterialGlobalIlluminationFlags.None));
            // An exactly black emission is flagged EmissiveIsBlack.
            Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(1f / 255f));
        }

        [Test]
        public void NpcTeamsNeverShareAColourWithAPlayer([NUnit.Framework.Range(1, DI_Rules.PlayersWithOwnColour)] int players) {
            const int npcTeams = 4;
            for (int npc = 0; npc < npcTeams; npc++) {
                TeamColor npcColor = DI_Rules.GetTeamColor(DI_Rules.NpcTeamIdStart + npc);
                Assert.That(npcColor, Is.Not.EqualTo(TeamColor.Grey), "Grey reads as a neutral base.");
                for (int player = 0; player < players; player++)
                    Assert.That(DI_Rules.GetTeamColor(player), Is.Not.EqualTo(npcColor),
                        $"Player {player + 1} and NPC team {npc + 1} are both {npcColor} with {players} players.");
            }
        }

        [Test]
        public void DarkTeamColoursAreLiftedToTheMinimumLuminance() {
            Color lifted = DI_Rules.Readable(Color.blue, DI_Rules.DefaultMinTeamLuminance);
            Assert.That(Luminance(lifted), Is.EqualTo(DI_Rules.DefaultMinTeamLuminance).Within(0.001f));
            Assert.That(lifted.b, Is.GreaterThan(lifted.r), "The hue must stay blue.");
        }

        [Test]
        public void LightTeamColoursAreUnchanged() {
            Color cyan = new(0.1f, 0.9f, 0.88f);
            Assert.That(DI_Rules.Readable(cyan, DI_Rules.DefaultMinTeamLuminance), Is.EqualTo(cyan));
        }
    }
}
