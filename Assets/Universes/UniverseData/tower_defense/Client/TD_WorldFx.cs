using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Shot and reward effects for Tower Defense, pooled: bullet tracers and sniper beams (line
    /// renderers), cannon shells and frost bolts (projectiles that fly to the target and burst), muzzle
    /// flashes, impacts and "+$" popups over destroyed enemies. Purely cosmetic; the server already
    /// applied the damage.
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

        struct Popup {
            public TMP_Text text;
            public Vector3 origin;
            public float start;
        }

        const float PopupSeconds = 1.1f;

        readonly TD_EffectSet set;
        readonly Transform parent;
        readonly Stack<LineRenderer> linePool = new();
        readonly Stack<Transform> projectilePool = new();
        readonly Stack<TMP_Text> popupPool = new();
        readonly List<Line> lines = new();
        readonly List<Flight> flights = new();
        readonly List<Popup> popups = new();
        readonly MaterialPropertyBlock block;

        public TD_WorldFx(TD_EffectSet set, Transform parent) {
            this.set = set;
            this.parent = parent;
            block = new MaterialPropertyBlock();
        }

        /// <summary>Plays one shot from <paramref name="muzzle"/> at <paramref name="target"/>.</summary>
        public void Shot(TD_TowerKind kind, Vector3 muzzle, Transform target, Vector3 aim) {
            Burst(set.muzzleFlash, muzzle, Quaternion.LookRotation(aim - muzzle), kind == TD_TowerKind.Gunner ? 0.6f : 1f);
            switch (kind) {
                case TD_TowerKind.Cannon:
                    Launch(kind, muzzle, target, aim, 0.45f, 3.5f);
                    break;
                case TD_TowerKind.Frost:
                    Launch(kind, muzzle, target, aim, 0.3f, 0.6f);
                    break;
                case TD_TowerKind.Sniper:
                    Trace(muzzle, aim, TD_Palette.Tower(kind), 0.22f, 0.22f);
                    Burst(set.impact, aim, Quaternion.identity, 1f);
                    break;
                default:
                    Trace(muzzle, aim, TD_Palette.Tower(kind), 0.09f, 0.06f);
                    break;
            }
        }

        /// <summary>A destroyed enemy: explosion and a rising reward popup.</summary>
        public void Destroyed(Vector3 position, int reward) {
            Burst(set.explosion, position, Quaternion.identity, 1f);
            if (set.rewardPopup == null) return;
            var text = popupPool.Count > 0 ? popupPool.Pop() : Object.Instantiate(set.rewardPopup, parent).GetComponent<TMP_Text>();
            if (text == null) return;
            text.gameObject.SetActive(true);
            text.text = $"+${reward}";
            var origin = position + Vector3.up * 3f;
            text.transform.position = origin;
            popups.Add(new Popup { text = text, origin = origin, start = Time.time });
        }

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
                Burst(f.kind == TD_TowerKind.Frost ? set.frostImpact : set.explosion, f.lastTarget, Quaternion.identity, f.kind == TD_TowerKind.Cannon ? 0.7f : 1f);
                f.projectile.gameObject.SetActive(false);
                projectilePool.Push(f.projectile);
                flights.RemoveAt(i);
            }
            for (int i = popups.Count - 1; i >= 0; i--) {
                var p = popups[i];
                float t = (now - p.start) / PopupSeconds;
                if (t >= 1f) {
                    p.text.gameObject.SetActive(false);
                    popupPool.Push(p.text);
                    popups.RemoveAt(i);
                    continue;
                }
                p.text.transform.position = p.origin + Vector3.up * (2.5f * t);
                p.text.alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
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

        void Launch(TD_TowerKind kind, Vector3 from, Transform target, Vector3 aim, float seconds, float arc) {
            if (set.projectile == null) {
                Burst(kind == TD_TowerKind.Frost ? set.frostImpact : set.explosion, aim, Quaternion.identity, 1f);
                return;
            }
            var projectile = projectilePool.Count > 0 ? projectilePool.Pop() : Object.Instantiate(set.projectile, parent).transform;
            projectile.gameObject.SetActive(true);
            projectile.position = from;
            projectile.localScale = Vector3.one * (kind == TD_TowerKind.Cannon ? 0.7f : 0.5f);
            Color color = TD_Palette.Tower(kind);
            foreach (var r in projectile.GetComponentsInChildren<Renderer>()) {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 2f);
                r.SetPropertyBlock(block);
                if (r is TrailRenderer trail) {
                    trail.Clear();
                    trail.startColor = color;
                    trail.endColor = new Color(color.r, color.g, color.b, 0f);
                }
            }
            flights.Add(new Flight { projectile = projectile, from = from, target = target, lastTarget = aim, start = Time.time, duration = seconds, arc = arc, kind = kind });
        }

        void Burst(GameObject prefab, Vector3 position, Quaternion rotation, float scale) {
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, position, rotation, parent);
            go.transform.localScale *= scale;
            Object.Destroy(go, set.effectLifetime);
        }
    }
}
