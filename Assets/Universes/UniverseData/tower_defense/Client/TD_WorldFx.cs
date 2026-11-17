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
            public Projectile projectile;
            public Vector3 from;
            public Transform target;
            public Vector3 lastTarget;
            public float start, duration, arc;
            public TD_TowerKind kind;
        }

        /// <summary>A kill reward waiting briefly for nearby kills, so a rush shows one summed "+$" popup.</summary>
        struct Reward {
            public Vector3 position;
            public int amount;
            public float showAt;
        }

        struct Line {
            public LineRenderer line;
            public float start, until, width;
        }

        /// <summary>A pooled shell or bolt with its renderers looked up once, not per shot.</summary>
        sealed class Projectile {
            public Transform transform;
            public Renderer[] renderers;
            public TrailRenderer[] trails;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly TD_EffectSet set;
        readonly TD_FxTuning tuning;
        readonly Transform parent;
        readonly Stack<LineRenderer> linePool = new();
        readonly Stack<Projectile> projectilePool = new();
        readonly List<Line> lines = new();
        readonly List<Flight> flights = new();
        readonly List<Reward> rewards = new();
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
                    Launch(kind, muzzle, target, aim, tuning.cannonFlight, tuning.cannonArc, tuning.cannonShellScale);
                    break;
                case TD_TowerKind.Frost:
                    Launch(kind, muzzle, target, aim, tuning.frostFlight, tuning.frostArc, tuning.frostBoltScale);
                    break;
                case TD_TowerKind.Sniper:
                    Trace(muzzle, aim, color, tuning.sniperBeamWidth, tuning.sniperBeamSeconds);
                    Land(kind, aim);
                    break;
                default:
                    Trace(muzzle, aim, color, tuning.tracerWidth, tuning.tracerSeconds);
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
                GameFx.Play(FxKind.Smoke, position, tuning.smokeTint, fx.debrisScale);
            }
            GameSfx.PlayAt(fx.largeExplosion ? SfxCue.ExplosionLarge : SfxCue.Explosion, position);
            if (fx.shake > 0f) CameraShake.Explosion(position, tuning.shakeRadius, fx.shake);
            coinLadder.PlayAt(position);
            AddReward(position, reward);
        }

        /// <summary>Adds to a pending popup nearby, or starts one; <see cref="ShowRewards"/> shows it when its window ends.</summary>
        void AddReward(Vector3 position, int reward) {
            float radius2 = tuning.rewardMergeRadius * tuning.rewardMergeRadius;
            for (int i = 0; i < rewards.Count; i++) {
                var r = rewards[i];
                if ((r.position - position).sqrMagnitude > radius2) continue;
                r.amount += reward;
                r.position = Vector3.Lerp(r.position, position, 0.5f);
                rewards[i] = r;
                return;
            }
            rewards.Add(new Reward { position = position, amount = reward, showAt = Time.unscaledTime + tuning.rewardMergeSeconds });
        }

        void ShowRewards() {
            float now = Time.unscaledTime;
            for (int i = rewards.Count - 1; i >= 0; i--) {
                var r = rewards[i];
                if (now < r.showAt) continue;
                Vector2 spread = Random.insideUnitCircle * tuning.rewardPopupSpread;
                Vector3 popup = r.position + new Vector3(spread.x, tuning.rewardPopupHeight, spread.y);
                FloatingText.Money(popup, r.amount, tuning.RewardPopupSize(r.amount));
                rewards.RemoveAt(i);
            }
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
            GameFx.Play(FxKind.DustPuff, pad, tuning.dustTint, tuning.buildDustScale);
            GameFx.Play(FxKind.ShockwaveRing, pad + Vector3.up * tuning.ringLift, TD_Palette.Tower(kind), tuning.buildDustScale);
            GameSfx.PlayAt(SfxCue.Build, pad);
            if (tower != null) Punch.Pop(tower);
        }

        public void Upgraded(Transform tower, Vector3 pad) {
            GameFx.Play(FxKind.ShockwaveRing, pad + Vector3.up * tuning.ringLift, tuning.levelUpColor, tuning.levelUpScale);
            GameFx.Play(FxKind.Sparks, pad + Vector3.up * tuning.levelUpSparkHeight, tuning.levelUpColor, tuning.levelUpScale);
            GameSfx.PlayAt(SfxCue.LevelUp, pad);
            if (tower != null) Punch.Squash(tower, tuning.levelUpSquash);
        }

        public void Sold(Vector3 pad, int refund) {
            GameFx.Play(FxKind.DustPuff, pad, tuning.dustTint, tuning.buildDustScale);
            GameFx.Play(FxKind.CoinBurst, pad + Vector3.up, tuning.sellBurstScale);
            GameSfx.PlayAt(SfxCue.Purchase, pad);
            FloatingText.Money(pad + Vector3.up * tuning.rewardPopupHeight, refund, tuning.sellPopupSize);
        }

        // ---------------------------------------------------------------- per frame
        public void Update() {
            ShowRewards();
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
                if (f.target != null && f.target.gameObject.activeInHierarchy) f.lastTarget = f.target.position + Vector3.up * tuning.aimHeight;
                float t = Mathf.Clamp01((now - f.start) / f.duration);
                Vector3 p = Vector3.Lerp(f.from, f.lastTarget, t) + Vector3.up * (f.arc * 4f * t * (1f - t));
                if (t < 1f) {
                    f.projectile.transform.position = p;
                    flights[i] = f;
                    continue;
                }
                Land(f.kind, f.lastTarget);
                f.projectile.transform.gameObject.SetActive(false);
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
            line.endColor = Color.Lerp(color, Color.white, tuning.tracerTipWhiten);
            line.widthMultiplier = width;
            lines.Add(new Line { line = line, start = Time.time, until = Time.time + seconds, width = width });
        }

        void Launch(TD_TowerKind kind, Vector3 from, Transform target, Vector3 aim, float seconds, float arc, float scale) {
            if (set.projectile == null) {
                Land(kind, aim);
                return;
            }
            var projectile = projectilePool.Count > 0 ? projectilePool.Pop() : NewProjectile();
            var root = projectile.transform;
            root.gameObject.SetActive(true);
            root.position = from;
            root.localScale = Vector3.one * scale;
            Color color = TD_Palette.Tower(kind);
            foreach (var r in projectile.renderers) {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(EmissionColorId, color * tuning.projectileGlow);
                r.SetPropertyBlock(block);
            }
            foreach (var trail in projectile.trails) {
                trail.Clear();
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
            flights.Add(new Flight { projectile = projectile, from = from, target = target, lastTarget = aim, start = Time.time, duration = seconds, arc = arc, kind = kind });
        }

        Projectile NewProjectile() {
            var go = Object.Instantiate(set.projectile, parent);
            return new Projectile {
                transform = go.transform,
                renderers = go.GetComponentsInChildren<Renderer>(true),
                trails = go.GetComponentsInChildren<TrailRenderer>(true),
            };
        }
    }
}
