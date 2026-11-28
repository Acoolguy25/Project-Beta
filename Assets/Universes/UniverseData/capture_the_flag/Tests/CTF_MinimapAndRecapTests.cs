using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Tests {
    /// <summary>The tactical minimap's maths and the respawn card's fight recap.</summary>
    public sealed class CTF_MinimapAndRecapTests {
        static readonly Rect Field = Rect.MinMaxRect(-120f, -72f, 120f, 72f);

        [Test]
        public void EachTeamSeesItsOwnBaseOnTheLeft() {
            Vector3 redStand = new(-92f, 2.4f, 0f), blueStand = new(92f, 2.4f, 0f);
            Assert.That(CTF_MapMath.ToMap(redStand, Field, CTF_MapMath.Flipped(CTF_Team.Red)).x, Is.LessThan(0.5f));
            Assert.That(CTF_MapMath.ToMap(blueStand, Field, CTF_MapMath.Flipped(CTF_Team.Blue)).x, Is.LessThan(0.5f));
            Assert.That(CTF_MapMath.ToMap(Vector3.zero, Field, true), Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Vector2 corner = CTF_MapMath.ToMap(new Vector3(-120f, 0f, 72f), Field, false);
            Assert.That(corner, Is.EqualTo(new Vector2(0f, 1f)), "north-west is the top left for Red");
            Assert.That(CTF_MapMath.ToMap(new Vector3(-120f, 0f, 72f), Field, true), Is.EqualTo(new Vector2(1f, 0f)), "and the bottom right for Blue");
        }

        [Test]
        public void PointsOutsideTheFenceStayOnTheRim() {
            Vector2 p = CTF_MapMath.ToMap(new Vector3(400f, 0f, -300f), Field, false);
            Assert.That(p, Is.EqualTo(new Vector2(1f, 0f)));
        }

        [Test]
        public void ArrowFollowsTheCameraOnBothMaps() {
            Assert.That(CTF_MapMath.ArrowDegrees(Vector3.forward, false), Is.EqualTo(0f).Within(1e-3f), "+z is up");
            Assert.That(CTF_MapMath.ArrowDegrees(Vector3.right, false), Is.EqualTo(270f).Within(1e-3f), "+x is right: a clockwise quarter");
            Assert.That(CTF_MapMath.ArrowDegrees(Vector3.right, true), Is.EqualTo(90f).Within(1e-3f), "Blue's map is turned: +x is left");
            Assert.That(CTF_MapMath.ArrowDegrees(Vector3.down, false), Is.EqualTo(0f), "looking straight down keeps a heading");
        }

        [Test]
        public void SpottedEnemiesFadeAndRadarOutlastsAShot() {
            Assert.That(CTF_MapMath.SpotAlpha(1f, 3f, 0.5f), Is.EqualTo(1f));
            Assert.That(CTF_MapMath.SpotAlpha(2.75f, 3f, 0.5f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(CTF_MapMath.SpotAlpha(3f, 3f, 0.5f), Is.Zero);
            Assert.That(CTF_MapMath.Spot(10f, 1f, 2.5f), Is.EqualTo(10f), "a shot never cuts a sweep short");
            Assert.That(CTF_MapMath.Spot(0f, 1f, 2.5f), Is.EqualTo(3.5f));
            Assert.That(CTF_MapMath.EnemyShown(true, false, 0f), Is.True, "your flag's carrier always shows");
            Assert.That(CTF_MapMath.EnemyShown(false, false, 0f), Is.False);
        }

        [Test]
        public void RecapCountsOnlyYourFightsThisLife() {
            const int me = 3, rivet = 1_000_002, bolt = 1_000_003;
            var recap = new CTF_DeathRecap();
            recap.Hit(me, rivet, me, 100);
            recap.Hit(me, me, rivet, 35);
            recap.Hit(me, rivet, me, 100);
            recap.Hit(me, bolt, 7, 100); // someone else's fight
            var duel = recap.Get(rivet);
            Assert.That((duel.hitsTaken, duel.damageTaken, duel.hitsDealt, duel.damageDealt), Is.EqualTo((2, 200, 1, 35)));
            Assert.That(recap.Get(bolt).Any, Is.False);
            Assert.That(CTF_DeathRecap.FightLine(duel), Is.EqualTo("They hit you 2× (200) · You hit them 1× (35)"));
            recap.Clear();
            Assert.That(recap.Get(rivet).Any, Is.False, "a new life starts clean");
        }

        [Test]
        public void RecapWordsOneSidedFightsRangeAndPayback() {
            Assert.That(CTF_DeathRecap.FightLine(new CTF_Duel { hitsTaken = 1, damageTaken = 150 }), Is.EqualTo("They hit you 1× (150) · you never landed a hit"));
            Assert.That(CTF_DeathRecap.FightLine(default), Is.Empty);
            Assert.That(CTF_DeathRecap.RangeText(33.6f, 8f), Is.EqualTo("from 34 m"));
            Assert.That(CTF_DeathRecap.RangeText(4f, 8f), Is.EqualTo("up close"));
            Assert.That(CTF_DeathRecap.PaybackLine(new CTF_Duel { damageDealt = 100 }, 200, 0.5f, 15), Does.StartWith("So close!"));
            Assert.That(CTF_DeathRecap.PaybackLine(new CTF_Duel { damageDealt = 35 }, 200, 0.5f, 15), Is.EqualTo("Get even: eliminate them for <b>Payback</b> +15 XP"));
        }
    }
}
