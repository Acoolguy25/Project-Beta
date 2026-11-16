using System.Collections.Generic;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Tower Defense's world effects. Its own, pooled: bullet tracers and sniper beams (line
    /// renderers) and cannon shells and frost bolts (projectiles that fly to the target). Everything
    /// else (muzzle sparks, blasts, wrecks, "+$" popups, sounds, shake) goes through the shared game
    /// feel kit, tuned by <see cref="TD_FxTuning"/>. Purely cosmetic; the server already applied
    /// the damage.
    /// </summary>
    public sealed class TD_WorldFx {
        struct Flight {
            public Transform projectile;
            public Vector3 from;
            public Transform target;
            public Vector3 lastTarget;
            public float start, duration, arc;
            public TD_TowerKind kind;
        }

        struct Line {
            public LineRenderer line;
            public float start, until, width;
        }

        /// <summary>Shot shapes per tower kind: flight time and arc for shells, width and life for lines.</summary>
        const float CannonFlight = 0.45f, CannonArc = 3.5f, CannonShellScale = 0.7f;
        const float FrostFlight = 0.3f, FrostArc = 0.6f, FrostBoltScale = 0.5f;
        const float SniperBeamWidth = 0.22f, SniperBeamSeconds = 0.22f, TracerWidth = 0.09f, TracerSeconds = 0.06f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly TD_EffectSet set;
        readonly TD_FxTuning tuning;
        readonly Transform parent;
        readonly Stack<LineRenderer> linePool = new();
        readonly Stack<Transform> projectilePool = new();
        readonly List<Line> lines = new();
        readonly List<Flight> flights = new();
        readonly MaterialPropertyBlock block = new();
        readonly SfxLadder coinLadder;

        public TD_WorldFx(TD_EffectSet set, TD_FxTuning tuning, Transform parent) {
            this.set = set;
            this.tuning = tuning;
            this.parent = parent;
            coinLadder = new SfxLadder(SfxCue.Coin, tuning.coinLadderStep, tuning.coinLadderSteps);
        }

        // ---------------------------------------------------------------- shots
        /// <summary>Plays one shot from <paramref name="muzzle"/> at <paramref name="target"/>.</summary>
        public void Shot(TD_TowerKind kind, Vector3 muzzle, Transform target, Vector3 aim) {
            var fx = tuning.Tower(kind);
            Color color = TD_Palette.Tower(kind);
            if (fx.muzzleSparks > 0f) GameFx.Play(FxKind.Sparks, muzzle, aim - muzzle, color, fx.muzzleSparks);
            GameSfx.PlayAt(fx.fire, muzzle);
            switch (kind) {
                case TD_TowerKind.Cannon:
                    Launch(kind, muzzle, target, aim, CannonFlight, CannonArc, CannonShellScale);
                    break;
                case TD_TowerKind.Frost:
                    Launch(kind, muzzle, target, aim, FrostFlight, FrostArc, FrostBoltScale);
                    break;
                case TD_TowerKind.Sniper:
                    Trace(muzzle, aim, color, SniperBeamWidth, SniperBeamSeconds);
                    Land(kind, aim);
                    break;
                default:
                    Trace(muzzle, aim, color, TracerWidth, TracerSeconds);
                    break;
            }
        }

        /// <summary>Where a shell, bolt or round lands.</summary>
        void Land(TD_TowerKind kind, Vector3 at) {
            Color color = TD_Palette.Tower(kind);
            switch (kind) {
                case TD_TowerKind.Cannon:
                    GameFx.Play(FxKind.ExplosionSmall, at, tuning.cannonBlastScale);
                    break;
                case TD_TowerKind.Frost:
                    GameFx.Play(FxKind.ShockwaveRing, at, color, tuning.frostBurstScale);
                    break;
                default:
                    GameFx.Play(FxKind.Sparks, at, color, tuning.sniperHitScale);
                    break;
            }
            GameSfx.PlayAt(tuning.Tower(kind).impact, at);
        }

        // ---------------------------------------------------------------- enemies
        /// <summary>An enemy destroyed by the towers: blast, debris, shake, coin and a "+$" popup.</summary>
        public void Destroyed(TD_EnemyKind kind, Vector3 position, int reward) {
            var fx = tuning.Enemy(kind);
            GameFx.Play(fx.largeExplosion ? FxKind.ExplosionLarge : FxKind.ExplosionSmall, position, fx.explosionScale);
            if (fx.debrisScale > 0f) {
                GameFx.Play(FxKind.Sparks, position, tuning.debrisTint, fx.debrisScale);
                GameFx.Play(FxKind.Smoke, position, fx.debrisScale);
            }
            GameSfx.PlayAt(fx.largeExplosion ? SfxCue.ExplosionLarge : SfxCue.Explosion, position);
            if (fx.shake > 0f) CameraShake.Explosion(position, tuning.shakeRadius, fx.shake);
            Vector2 spread = Random.insideUnitCircle * tuning.rewardPopupSpread;
            Vector3 popup = position + new Vector3(spread.x, tuning.rewardPopupHeight + Random.value * tuning.rewardPopupSpread * 0.5f, spread.y);
            FloatingText.Money(popup, reward, tuning.rewardPopupSize);
            coinLadder.PlayAt(popup);
        }

        /// <summary>An enemy that reached the base: it blows up against the walls, costing health, not paying.</summary>
        public void Leaked(TD_EnemyKind kind, Vector3 position) {
            var fx = tuning.Enemy(kind);
            GameFx.Play(fx.largeExplosion ? FxKind.ExplosionLarge : FxKind.ExplosionSmall, position, fx.explosionScale);
            GameSfx.PlayAt(SfxCue.Explosion, position);
            CameraShake.Kick(tuning.leakShake);
        }

        // ---------------------------------------------------------------- towers
        public void Built(Transform tower, Vector3 pad, TD_TowerKind kind) {
            GameFx.Play(FxKind.DustPuff, pad, tuning.buildDustScale);
            // Lifted a little so the pad's own top doesn't swallow the flat ring.
            GameFx.Play(FxKind.ShockwaveRing, pad + Vector3.up * 0.25f, TD_Palette.Tower(kind), tuning.buildDustScale);
            GameSfx.PlayAt(SfxCue.Build, pad);
            if (tower != null) Punch.Pop(tower);
        }

        public void Upgraded(Transform tower, Vector3 pad) {
            GameFx.Play(FxKind.ShockwaveRing, pad + Vector3.up * 0.25f, tuning.levelUpColor, tuning.levelUpScale);
            GameFx.Play(FxKind.Sparks, pad + Vector3.up * 2f, tuning.levelUpColor, tuning.levelUpScale);
            GameSfx.PlayAt(SfxCue.LevelUp, pad);
            if (tower != null) Punch.Squash(tower, -0.2f);
        }

        public void Sold(Vector3 pad, int refund) {
            GameFx.Play(FxKind.DustPuff, pad, tuning.buildDustScale);
            GameFx.Play(FxKind.CoinBurst, pad + Vector3.up, tuning.sellBurstScale);
            GameSfx.PlayAt(SfxCue.Purchase, pad);
            FloatingText.Money(pad + Vector3.up * tuning.rewardPopupHeight, refund, tuning.sellPopupSize);
        }

        // ---------------------------------------------------------------- per frame
        public void Update() {
            float now = Time.time;
            for (int i = lines.Count - 1; i >= 0; i--) {
                var l = lines[i];
                float t = Mathf.InverseLerp(l.start, l.until, now);
                l.line.widthMultiplier = l.width * (1f - t);
                if (now < l.until) continue;
                l.line.gameObject.SetActive(false);
                linePool.Push(l.line);
                lines.RemoveAt(i);
            }
            for (int i = flights.Count - 1; i >= 0; i--) {
                var f = flights[i];
                if (f.target != null && f.target.gameObject.activeInHierarchy) f.lastTarget = f.target.position + Vector3.up;
                float t = Mathf.Clamp01((now - f.start) / f.duration);
                Vector3 p = Vector3.Lerp(f.from, f.lastTarget, t) + Vector3.up * (f.arc * 4f * t * (1f - t));
                if (t < 1f) {
                    f.projectile.position = p;
                    flights[i] = f;
                    continue;
                }
                Land(f.kind, f.lastTarget);
                f.projectile.gameObject.SetActive(false);
                projectilePool.Push(f.projectile);
                flights.RemoveAt(i);
            }
        }

        void Trace(Vector3 from, Vector3 to, Color color, float width, float seconds) {
            if (set.tracer == null) return;
            var line = linePool.Count > 0 ? linePool.Pop() : Object.Instantiate(set.tracer, parent).GetComponent<LineRenderer>();
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = Color.Lerp(color, Color.white, 0.6f);
            line.widthMultiplier = width;
            lines.Add(new Line { line = line, start = Time.time, until = Time.time + seconds, width = width });
        }

        void Launch(TD_TowerKind kind, Vector3 from, Transform target, Vector3 aim, float seconds, float arc, float scale) {
            if (set.projectile == null) {
                Land(kind, aim);
                return;
            }
            var projectile = projectilePool.Count > 0 ? projectilePool.Pop() : Object.Instantiate(set.projectile, parent).transform;
            projectile.gameObject.SetActive(true);
            projectile.position = from;
            projectile.localScale = Vector3.one * scale;
            Color color = TD_Palette.Tower(kind);
            foreach (var r in projectile.GetComponentsInChildren<Renderer>()) {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(EmissionColorId, color * 2f);
                r.SetPropertyBlock(block);
                if (r is TrailRenderer trail) {
                    trail.Clear();
                    trail.startColor = color;
                    trail.endColor = new Color(color.r, color.g, color.b, 0f);
                }
            }
            flights.Add(new Flight { projectile = projectile, from = from, target = target, lastTarget = aim, start = Time.time, duration = seconds, arc = arc, kind = kind });
        }
    }
}
