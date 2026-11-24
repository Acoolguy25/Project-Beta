using System.Collections.Generic;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Tower Defense's world effects. Its own, pooled: bullet tracers, sniper beams and Tesla lightning
    /// (line renderers), cannon shells, frost bolts and mortar rounds (projectiles that fly to the
    /// target) and the Airstrike jet. Everything else (muzzle sparks, blasts, wrecks, shield breaks,
    /// heal sparkles, "+$" popups, sounds, shake) goes through the shared game feel kit, tuned by
    /// <see cref="TD_FxTuning"/>. Purely cosmetic; the server already applied the damage.
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
            public float start, until, width, nextFlicker;
            /// <summary>Lightning: re-jittered between these ends while it lives.</summary>
            public bool jagged;
            public Vector3 from, to;
        }

        /// <summary>Something that happens a moment later (an airstrike's bombs).</summary>
        struct Timed {
            public float at;
            public Vector3 position;
            public bool last;
        }

        /// <summary>The Airstrike jet crossing the map.</summary>
        struct Jet {
            public Transform transform;
            public Vector3 from, to;
            public float start, duration;
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
        readonly List<Timed> timed = new();
        readonly List<Jet> jets = new();
        float lastShieldText = float.NegativeInfinity, lastBlockedText = float.NegativeInfinity;
        readonly Stack<Transform> jetPool = new();
        readonly Stack<LineRenderer> arcPool = new();
        readonly MaterialPropertyBlock block = new();
        readonly SfxLadder coinLadder;
        /// <summary>Keeps popups off the HUD's top band; null = popups go where they happen.</summary>
        public TD_ScreenGuard guard;

        public TD_WorldFx(TD_EffectSet set, TD_FxTuning tuning, Transform parent) {
            this.set = set;
            this.tuning = tuning;
            this.parent = parent;
            coinLadder = new SfxLadder(SfxCue.Coin, tuning.coinLadderStep, tuning.coinLadderSteps);
        }

        /// <summary>Where a popup of <paramref name="text"/> length that rises <paramref name="rise"/> m can spawn clear of the HUD.</summary>
        Vector3 Clear(Vector3 at, float rise, int characters) => guard != null ? guard.Clear(at, rise, characters) : at;

        static FloatingTextStyle Style(System.Func<GameFeelSettings.FloatingTextTuning, FloatingTextStyle> pick) {
            var settings = GameFeelSettings.Active;
            return settings != null ? pick(settings.floatingText) : default;
        }

        void Money(Vector3 at, int amount, float size) {
            int digits = Mathf.Max(1, Mathf.FloorToInt(Mathf.Log10(Mathf.Max(1, amount))) + 1);
            FloatingText.Money(Clear(at, Style(t => t.money).rise, digits + 2), amount, size);
        }

        void Shout(Vector3 at, string text, Color color, float size) =>
            FloatingText.Callout(Clear(at, Style(t => t.callout).rise, text.Length), text, color, size);

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
                case TD_TowerKind.Tesla:
                    Arc(muzzle, aim, color);
                    Land(kind, aim);
                    break;
                case TD_TowerKind.Mortar:
                    Launch(kind, muzzle, target, aim, tuning.mortarFlight, tuning.mortarArc, tuning.mortarShellScale);
                    break;
                default:
                    Trace(muzzle, aim, color, tuning.tracerWidth, tuning.tracerSeconds);
                    break;
            }
        }

        /// <summary>A Tesla bolt jumping from one enemy to the next (no muzzle sparks or fire sound: one zap per chain).</summary>
        public void Chain(Vector3 from, Vector3 to) {
            Color color = TD_Palette.Tower(TD_TowerKind.Tesla);
            Arc(from, to, color);
            Land(TD_TowerKind.Tesla, to);
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
                case TD_TowerKind.Tesla:
                    GameFx.Play(FxKind.Sparks, at, color, tuning.arcHitScale);
                    break;
                case TD_TowerKind.Mortar:
                    GameFx.Play(FxKind.ExplosionLarge, at, tuning.mortarBlastScale);
                    GameFx.Play(FxKind.DustPuff, at, tuning.dustTint, tuning.mortarBlastScale);
                    if (tuning.mortarShake > 0f) CameraShake.Explosion(at, tuning.shakeRadius, tuning.mortarShake);
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
            if (TD_Rules.Enemy(kind).splits > 0) SplitBurst(position);
            coinLadder.PlayAt(position);
            AddReward(position, reward);
        }

        /// <summary>A Carrier breaking open: a ring and sparks in the drone colour and a callout, so the swarm it lets loose reads.</summary>
        void SplitBurst(Vector3 position) {
            if (tuning.carrierBurstScale <= 0f) return;
            Color color = TD_Palette.Enemy(TD_EnemyKind.Drone);
            GameFx.Play(FxKind.ShockwaveRing, position + Vector3.up * tuning.ringLift, color, tuning.carrierBurstScale);
            GameFx.Play(FxKind.Sparks, position, color, tuning.carrierBurstScale * 0.6f);
            if (!string.IsNullOrEmpty(tuning.carrierBurstText))
                Shout(position + Vector3.up * tuning.rewardPopupHeight, tuning.carrierBurstText, color, tuning.carrierBurstTextSize);
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
                Money(popup, r.amount, tuning.RewardPopupSize(r.amount));
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

        /// <summary>A shield broke: a burst in the shield colour, a crack and a "SHIELD DOWN" callout.</summary>
        public void ShieldBroken(Vector3 position) {
            Color color = TD_Palette.Shield;
            GameFx.Play(FxKind.ShockwaveRing, position, color, tuning.shieldBreakScale);
            GameFx.Play(FxKind.Sparks, position, color, tuning.shieldBreakScale);
            GameSfx.PlayAt(tuning.shieldBreakSound, position);
            if (string.IsNullOrEmpty(tuning.shieldBreakText) || Time.time - lastShieldText < tuning.shieldBreakTextGap) return;
            lastShieldText = Time.time;
            Shout(position + Vector3.up * tuning.rewardPopupHeight, tuning.shieldBreakText, color, tuning.shieldBreakTextSize);
        }

        /// <summary>A hit the shield soaked (its bar drops): sparks in the shield colour.</summary>
        public void ShieldSpark(Vector3 position) {
            if (tuning.shieldSparkScale > 0f) GameFx.Play(FxKind.Sparks, position, TD_Palette.Shield, tuning.shieldSparkScale);
        }

        /// <summary>
        /// A hit too weak to touch the shield: grey sparks glancing back toward the tower, a dull ping, and now
        /// and then a "BLOCKED" callout (one for all the towers bouncing, every few seconds).
        /// </summary>
        public void Ricochet(Vector3 position, Vector3 from) {
            if (tuning.ricochetScale > 0f) GameFx.Play(FxKind.Sparks, position, from - position, tuning.ricochetColor, tuning.ricochetScale);
            GameSfx.PlayAt(tuning.ricochetSound, position);
            if (string.IsNullOrEmpty(tuning.blockedText) || Time.time - lastBlockedText < tuning.blockedTextGap) return;
            // "BLOCKED" means "this shield": if the HUD guard would carry it far from the shield (onto another
            // enemy or tower), skip it; the sparks and ping still say it, and the next clear one shows.
            Vector3 at = position + Vector3.up * tuning.rewardPopupHeight;
            string text = tuning.blockedText;
            if (guard != null) {
                at = guard.Clear(at, Style(t => t.callout).rise, text.Length, out float shift);
                if (shift > tuning.blockedMaxShift) return;
            }
            lastBlockedText = Time.time;
            FloatingText.Callout(at, text, tuning.ricochetColor, tuning.blockedTextSize);
        }

        /// <summary>A medic is repairing this enemy (the controller rate-limits it per enemy).</summary>
        public void Healed(Vector3 position) {
            if (tuning.healFxScale > 0f) GameFx.Play(FxKind.Heal, position, TD_Palette.Heal, tuning.healFxScale);
        }

        // ---------------------------------------------------------------- abilities
        /// <summary>
        /// An Airstrike on its way: a marker on the road, the jet crossing the map along the road so it
        /// is over <paramref name="target"/> when the bombs land (<paramref name="seconds"/> from now),
        /// then a string of blasts across the strike radius.
        /// </summary>
        public void Airstrike(Vector3 target, Vector3 forward, float radius, float seconds, string caster) {
            Color color = TD_Palette.Ability(TD_Ability.Airstrike);
            GameFx.Play(FxKind.BeaconBeam, target, color, tuning.strikeMarkerScale);
            GameFx.Play(FxKind.ShockwaveRing, target + Vector3.up * tuning.ringLift, color, radius * tuning.strikeRingScale);
            Callout(target, "AIRSTRIKE", color);
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            seconds = Mathf.Max(0.05f, seconds);
            if (set.jet != null) {
                // Over the target at the moment the bombs land, then on out the other side.
                Vector3 over = target + Vector3.up * tuning.jetHeight;
                float lead = Mathf.Max(tuning.jetLeadIn, tuning.jetSpeed * seconds);
                float speed = lead / seconds;
                var jet = jetPool.Count > 0 ? jetPool.Pop() : Object.Instantiate(set.jet, parent).transform;
                jet.gameObject.SetActive(true);
                jet.localScale = Vector3.one * tuning.jetScale;
                jet.rotation = Quaternion.LookRotation(forward);
                var flight = new Jet { transform = jet, from = over - forward * lead, to = over + forward * lead, start = Time.time, duration = 2f * lead / speed };
                jet.position = flight.from;
                jets.Add(flight);
                GameSfx.PlayAt(tuning.jetSound, over);
            }
            float land = Time.time + seconds;
            for (int i = 0; i < tuning.strikeBlasts; i++) {
                // Bombs walk along the road through the target, as a stick of bombs from a pass would.
                float along = tuning.strikeBlasts == 1 ? 0f : Mathf.Lerp(-radius, radius, i / (tuning.strikeBlasts - 1f)) * tuning.strikeSpread;
                Vector2 jitter = Random.insideUnitCircle * radius * tuning.strikeJitter;
                Vector3 at = target + forward * along + new Vector3(jitter.x, 0f, jitter.y);
                timed.Add(new Timed { at = land + i * tuning.strikeBlastInterval, position = at, last = i == tuning.strikeBlasts - 1 });
            }
        }

        /// <summary>An EMP: a blue flash, a ring at the base and on every enemy it stopped, and a crackle.</summary>
        public void Emp(Vector3 origin, IReadOnlyList<Vector3> enemies, string caster) {
            Color color = TD_Palette.Ability(TD_Ability.Emp);
            ScreenFx.Flash(tuning.empFlash, tuning.empFlashSeconds);
            GameFx.Play(FxKind.ShockwaveRing, origin + Vector3.up * tuning.ringLift, color, tuning.empRingScale);
            foreach (var p in enemies) {
                GameFx.Play(FxKind.ShockwaveRing, p, color, tuning.empEnemyScale);
                GameFx.Play(FxKind.Sparks, p, color, tuning.empEnemyScale * 0.5f);
            }
            GameSfx.Play(tuning.empSound);
            CameraShake.Kick(tuning.leakShake);
            Callout(origin, "EMP", color);
        }

        void Callout(Vector3 at, string text, Color color) =>
            Shout(at + Vector3.up * tuning.abilityCalloutHeight, text, color, tuning.abilityCalloutSize);

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
            Money(pad + Vector3.up * tuning.rewardPopupHeight, refund, tuning.sellPopupSize);
        }

        // ---------------------------------------------------------------- per frame
        public void Update() {
            ShowRewards();
            float now = Time.time;
            for (int i = lines.Count - 1; i >= 0; i--) {
                var l = lines[i];
                float t = Mathf.InverseLerp(l.start, l.until, now);
                l.line.widthMultiplier = l.width * (1f - t);
                if (l.jagged && now >= l.nextFlicker && now < l.until) {
                    Jag(l.line, l.from, l.to);
                    l.nextFlicker = now + 1f / tuning.arcFlickerRate;
                    lines[i] = l;
                }
                if (now < l.until) continue;
                l.line.gameObject.SetActive(false);
                (l.jagged && set.arc != null ? arcPool : linePool).Push(l.line);
                lines.RemoveAt(i);
            }
            for (int i = timed.Count - 1; i >= 0; i--) {
                var e = timed[i];
                if (now < e.at) continue;
                GameFx.Play(FxKind.ExplosionLarge, e.position, tuning.strikeBlastScale);
                GameSfx.PlayAt(SfxCue.ExplosionLarge, e.position);
                if (e.last) CameraShake.Explosion(e.position, tuning.shakeRadius, tuning.strikeShake);
                timed.RemoveAt(i);
            }
            for (int i = jets.Count - 1; i >= 0; i--) {
                var j = jets[i];
                float t = (now - j.start) / j.duration;
                if (t < 1f) {
                    j.transform.position = Vector3.Lerp(j.from, j.to, t);
                    continue;
                }
                j.transform.gameObject.SetActive(false);
                jetPool.Push(j.transform);
                jets.RemoveAt(i);
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

        /// <summary>Tesla lightning: a jagged many-point line that re-jitters while it fades (the tracer if there is no arc prefab).</summary>
        void Arc(Vector3 from, Vector3 to, Color color) {
            if (set.arc == null) {
                Trace(from, to, color, tuning.arcWidth, tuning.arcSeconds);
                return;
            }
            var line = arcPool.Count > 0 ? arcPool.Pop() : Object.Instantiate(set.arc, parent).GetComponent<LineRenderer>();
            line.gameObject.SetActive(true);
            Jag(line, from, to);
            line.startColor = Color.Lerp(color, Color.white, tuning.tracerTipWhiten);
            line.endColor = color;
            line.widthMultiplier = tuning.arcWidth;
            float now = Time.time;
            lines.Add(new Line {
                line = line, start = now, until = now + tuning.arcSeconds, width = tuning.arcWidth,
                jagged = true, from = from, to = to, nextFlicker = now + 1f / tuning.arcFlickerRate,
            });
        }

        /// <summary>Lays a bolt from <paramref name="from"/> to <paramref name="to"/> with random kinks, straight at both ends.</summary>
        void Jag(LineRenderer line, Vector3 from, Vector3 to) {
            int n = Mathf.Max(2, line.positionCount);
            Vector3 dir = to - from;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;
            for (int i = 0; i < n; i++) {
                float t = i / (n - 1f);
                float strength = Mathf.Sin(t * Mathf.PI) * tuning.arcJitter; // 0 at both ends
                Vector3 kink = side * Random.Range(-strength, strength) + up * Random.Range(-strength, strength) * 0.5f;
                line.SetPosition(i, from + dir * t + kink);
            }
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
