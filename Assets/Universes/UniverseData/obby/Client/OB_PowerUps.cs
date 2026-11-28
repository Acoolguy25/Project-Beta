using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// The client side of "?" blocks: which blocks the local player has spent, the power-up pill on the HUD, the
    /// bubble and rainbow drawn round every racer who has one, and the movement side of the local player's powers
    /// (Cloud Boots' mid-air jump, Rainbow Rush's speed) for as long as the server says they last.
    /// The server decides every bump, pop and timer.
    /// </summary>
    public sealed class OB_PowerUps : MonoBehaviour {
        [Header("HUD pill")]
        public CanvasGroup pill;
        public RectTransform pillRect;
        public Image icon;
        public TMP_Text label;
        [Tooltip("Remaining time of a timed power: its anchor max x is set to the fraction left.")]
        public RectTransform timerFill;
        public Image timerFillImage;
        [Tooltip("HUD icons per power: shield, boots, rush.")]
        public Sprite shieldIcon, bootsIcon, rushIcon;
        [Min(0.01f)] public float pillFadeSeconds = 0.2f;

        [Header("Racer visuals")]
        [Tooltip("The bubble drawn round a racer: a unit-diameter glowing sphere (the rush tints it through a property block).")]
        public GameObject bubblePrefab;
        [Tooltip("Bubble diameter as a multiple of the racer's collider height.")]
        [Min(0.5f)] public float bubbleScale = 1.15f;
        [Tooltip("Hue cycles per second of the Rainbow Rush glow.")]
        [Min(0f)] public float rainbowSpeed = 1.5f;
        [Tooltip("Seconds of a timed power left when its pill starts blinking.")]
        [Min(0f)] public float warnSeconds = 3f;

        [Header("Local movement")]
        [Tooltip("Mid-air jumps per jump while Cloud Boots are on.")]
        [Min(1)] public int airJumps = 1;
        [Tooltip("Seconds after leaving the ground before a jump counts as a mid-air jump (the normal coyote jump comes first).")]
        [Min(0f)] public float airJumpDelay = 0.14f;

        /// <summary>One racer's powers as last told by the server, in local <see cref="Time.time"/>.</summary>
        struct Powers {
            public bool shield;
            public float bootsUntil, rushUntil;
            public bool Any(float now) => shield || now < bootsUntil || now < rushUntil;
        }

        sealed class Bubble {
            public GameObject go;
            public Renderer renderer;
        }

        /// <summary>The obby's mid-air jump and rush speed, registered on the local player's movement.</summary>
        sealed class PowerMovement : IMovementMode {
            public OB_PowerUps owner;
            int jumpsLeft;
            float groundedAt;

            public bool Step(CharacterMovement movement, float dt) {
                float now = Time.time;
                Powers mine = owner.LocalPowers;
                if (now < mine.rushUntil) movement.SetMoveBoost(owner.RushSpeed);
                if (movement.Grounded) {
                    jumpsLeft = owner.airJumps;
                    groundedAt = Time.fixedTime;
                    return false;
                }
                if (now >= mine.bootsUntil || !movement.JumpPressed || jumpsLeft <= 0 || movement.HasWallContact) return false;
                if (Time.fixedTime - groundedAt < owner.airJumpDelay) return false;
                if (movement.TryGetComponent(out CharacterStance stance) && stance.Current is not (Stance.Normal or Stance.Dash)) return false;
                jumpsLeft--;
                movement.Launch(movement.ExternalVelocity + movement.Up * movement.JumpSpeed);
                owner.OnAirJump(movement);
                return false;
            }
        }

        OB_ClientController controller;
        OB_Course course;
        ClientManager manager;
        readonly Dictionary<int, Powers> powers = new();
        readonly Dictionary<int, Bubble> bubbles = new();
        readonly List<int> staleBubbles = new();
        readonly PowerMovement movementMode = new();
        CharacterMovement registeredOn;
        MaterialPropertyBlock block;
        ulong usedBlocks;
        int shownSeed;
        float pillAlpha;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Powers LocalPowers => powers.TryGetValue(LocalClientId, out Powers p) ? p : default;
        float RushSpeed => Rules.rushSpeed;
        OB_PowerUpRules Rules => course != null && course.rules?.powerUps != null ? course.rules.powerUps : new OB_PowerUpRules();
        int LocalClientId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void Awake() {
            controller = GetComponent<OB_ClientController>();
            course = GetComponentInParent<OB_Course>();
            movementMode.owner = this;
            if (pill != null) pill.alpha = 0f;
        }

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<OB_PowerBroadcast>(OnPower);
            if (registeredOn != null) registeredOn.RemoveMode(movementMode);
            foreach (var bubble in bubbles.Values)
                if (bubble.go != null) Destroy(bubble.go);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<OB_PowerBroadcast>(OnPower);
            }
            // The movement side follows the local robot (a new one appears after each race).
            if (registeredOn != CharacterMovement.Local) {
                if (registeredOn != null) registeredOn.RemoveMode(movementMode);
                registeredOn = CharacterMovement.Local;
                // Before the parkour, dash and wall-slide modes: a dash still sets its own (faster) boost after the rush.
                if (registeredOn != null) registeredOn.AddMode(movementMode, 20);
            }
            // A rebuilt course starts with every block fresh until the server says otherwise.
            if (course != null && course.Layout != null && course.Layout.seed != shownSeed) {
                shownSeed = course.Layout.seed;
                ShowBlocks(false, -1);
            }
            RefreshPill();
        }

        void LateUpdate() => RefreshBubbles();

        void OnPower(OB_PowerBroadcast message, Channel channel) {
            if (course == null || course.Layout == null || message.seed != course.Layout.seed) return;
            float now = Time.time;
            Powers before = powers.TryGetValue(message.clientId, out Powers known) ? known : default;
            var after = new Powers {
                shield = message.shield,
                bootsUntil = message.bootsLeft > 0f ? now + message.bootsLeft : 0f,
                rushUntil = message.rushLeft > 0f ? now + message.rushLeft : 0f
            };
            powers[message.clientId] = after;
            bool mine = message.clientId == LocalClientId;
            Vector3? at = CentreOf(message.clientId);

            if (mine) {
                usedBlocks = message.usedBlocks;
                ShowBlocks(message.evt == OB_PowerEvent.Got, message.stage);
            }
            switch (message.evt) {
                case OB_PowerEvent.Got:
                    Celebrate(message.gained, message.stage, mine);
                    break;
                case OB_PowerEvent.ShieldPopped when before.shield:
                    if (at.HasValue) {
                        GameFx.Play(FxKind.ShockwaveRing, at.Value, OB_PowerUpInfo.Colour(OB_PowerUp.Shield), 1.4f);
                        GameFx.Play(FxKind.Sparks, at.Value, OB_PowerUpInfo.Colour(OB_PowerUp.Shield), 1.2f);
                        GameSfx.PlayAt(SfxCue.Hit, at.Value, 0.9f, GameSfx.Semitones(5f));
                    }
                    if (mine) {
                        GameSfx.Play(SfxCue.Whoosh, 0.7f, GameSfx.Semitones(4f));
                        CameraShake.Kick(0.25f, 0.2f);
                        ScreenFx.Flash(OB_PowerUpInfo.Colour(OB_PowerUp.Shield).WithAlpha(0.25f));
                        if (controller != null) controller.Toast("BUBBLE POPPED!  <size=75%><alpha=#CC>THE SHIELD SAVED YOU</size>", OB_PowerUpInfo.Colour(OB_PowerUp.Shield), 2f);
                        if (pillRect != null) Punch.Pop(pillRect, 1.3f, 0.3f);
                    }
                    break;
            }
        }

        /// <summary>The bump: the block hops, the prize name floats up, and the bumper hears it.</summary>
        void Celebrate(OB_PowerUp power, int stage, bool mine) {
            Color colour = OB_PowerUpInfo.Colour(power);
            if (TryGetBlock(stage, out OB_PowerBlock view, out Vector3 top)) {
                GameFx.Play(FxKind.CoinBurst, top, colour, 1.3f);
                if (mine) {
                    GameFx.Play(FxKind.Confetti, top, colour, 0.8f);
                    FloatingText.Callout(top + Vector3.up * 1.5f, OB_PowerUpInfo.Name(power).ToUpperInvariant(), colour, 1.3f);
                }
                GameSfx.PlayAt(SfxCue.Coin, top, mine ? 0.6f : 0.4f, GameSfx.Semitones(-3f));
            }
            if (!mine) return;
            GameSfx.Play(SfxCue.LevelUp, 0.7f, GameSfx.Semitones(2f));
            CameraShake.Kick(0.15f, 0.15f);
            if (controller != null)
                controller.Toast($"{OB_PowerUpInfo.Name(power).ToUpperInvariant()}!  <size=75%><alpha=#CC>{OB_PowerUpInfo.Hint(power).ToUpperInvariant()}</size>", colour, 3f);
            if (pillRect != null) Punch.Pop(pillRect, 1.35f, 0.35f);
        }

        void OnAirJump(CharacterMovement movement) {
            Vector3 feet = movement.Body.position;
            GameFx.Poof(feet, movement.Up, 0.5f, OB_PowerUpInfo.Colour(OB_PowerUp.Boots), 1.6f);
            GameSfx.Play(SfxCue.Jump, 0.8f, GameSfx.Semitones(7f));
        }

        // ---------------------------------------------------------------- blocks
        void ShowBlocks(bool bumped, int bumpedStage) {
            if (course == null || course.Layout == null) return;
            var pieces = course.Layout.pieces;
            var instances = course.Instances;
            for (int i = 0; i < pieces.Count && i < instances.Count; i++) {
                if (pieces[i].kind != OB_PieceKind.PowerBlock || instances[i] == null) continue;
                if (!instances[i].TryGetComponent(out OB_PowerBlock view)) continue;
                int s = pieces[i].stage;
                bool used = s < 64 && (usedBlocks & (1UL << s)) != 0;
                view.SetUsed(used, bumped && s == bumpedStage);
            }
        }

        bool TryGetBlock(int stage, out OB_PowerBlock view, out Vector3 top) {
            view = null;
            top = default;
            if (course == null || course.Layout == null || stage < 0) return false;
            var pieces = course.Layout.pieces;
            var instances = course.Instances;
            for (int i = 0; i < pieces.Count && i < instances.Count; i++) {
                if (pieces[i].kind != OB_PieceKind.PowerBlock || pieces[i].stage != stage || instances[i] == null) continue;
                instances[i].TryGetComponent(out view);
                top = pieces[i].position + Vector3.up * (pieces[i].size.y * 0.5f);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- HUD
        void RefreshPill() {
            if (pill == null) return;
            float now = Time.time;
            Powers mine = LocalPowers;
            bool rush = now < mine.rushUntil, boots = now < mine.bootsUntil;
            bool any = mine.shield || rush || boots;
            pillAlpha = Mathf.MoveTowards(pillAlpha, any ? 1f : 0f, Time.unscaledDeltaTime / pillFadeSeconds);
            pill.alpha = pillAlpha;
            if (!any) return;

            // The pill leads with the timed power (it runs out); a held shield is named after it.
            OB_PowerUp lead = rush ? OB_PowerUp.Rush : boots ? OB_PowerUp.Boots : OB_PowerUp.Shield;
            float left = rush ? mine.rushUntil - now : boots ? mine.bootsUntil - now : 0f;
            float total = rush ? Rules.rushSeconds : boots ? Rules.bootsSeconds : 1f;
            Color colour = rush ? Rainbow(now) : OB_PowerUpInfo.Colour(lead);
            if (icon != null) {
                icon.sprite = lead switch { OB_PowerUp.Rush => rushIcon, OB_PowerUp.Boots => bootsIcon, _ => shieldIcon };
                icon.color = colour;
            }
            if (label != null) {
                string text = OB_PowerUpInfo.Name(lead).ToUpperInvariant();
                if (lead != OB_PowerUp.Shield) text += $"  <size=80%><alpha=#CC>{Mathf.CeilToInt(left)}s</size>";
                if (lead != OB_PowerUp.Shield && mine.shield) text += "  <size=70%><alpha=#CC>+ SHIELD</size>";
                label.text = text;
            }
            if (timerFill != null) {
                float fraction = lead == OB_PowerUp.Shield ? 1f : Mathf.Clamp01(left / Mathf.Max(0.01f, total));
                timerFill.anchorMax = new Vector2(fraction, timerFill.anchorMax.y);
                if (timerFillImage != null) {
                    // A timed power about to run out blinks.
                    bool warn = lead != OB_PowerUp.Shield && left < warnSeconds && Mathf.Repeat(now * 4f, 1f) < 0.5f;
                    timerFillImage.color = warn ? OB_Palette.Paper : colour;
                }
            }
        }

        Color Rainbow(float now) => Color.HSVToRGB(Mathf.Repeat(now * rainbowSpeed * 0.25f, 1f), 0.65f, 1f);

        // ---------------------------------------------------------------- racer visuals
        void RefreshBubbles() {
            float now = Time.time;
            staleBubbles.Clear();
            foreach (int id in bubbles.Keys) staleBubbles.Add(id);
            foreach (var pair in powers) {
                Powers p = pair.Value;
                bool rush = now < p.rushUntil;
                if (!p.shield && !rush) continue;
                if (!TryGetBody(pair.Key, out Collider body)) continue;
                staleBubbles.Remove(pair.Key);
                Bubble bubble = BubbleFor(pair.Key);
                if (bubble == null) continue;
                Bounds bounds = body.bounds;
                float pulse = 1f + 0.04f * Mathf.Sin(now * (rush ? 14f : 4f));
                float diameter = Mathf.Max(bounds.size.y, bounds.size.x) * bubbleScale * pulse;
                bubble.go.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
                bubble.go.transform.localScale = Vector3.one * diameter;
                Color colour = rush ? Rainbow(now + pair.Key * 0.37f) : OB_PowerUpInfo.Colour(OB_PowerUp.Shield);
                block ??= new MaterialPropertyBlock();
                bubble.renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, colour.WithAlpha(rush ? 0.14f : 0.16f));
                block.SetColor(EmissionColor, colour * (rush ? 0.9f : 0.8f));
                bubble.renderer.SetPropertyBlock(block);
                if (!bubble.go.activeSelf) bubble.go.SetActive(true);
            }
            foreach (int id in staleBubbles)
                if (bubbles.TryGetValue(id, out Bubble stale) && stale.go != null && stale.go.activeSelf) stale.go.SetActive(false);
        }

        Bubble BubbleFor(int clientId) {
            if (bubbles.TryGetValue(clientId, out Bubble bubble) && bubble.go != null) return bubble;
            if (bubblePrefab == null) return null;
            var go = Instantiate(bubblePrefab, transform);
            go.name = $"PowerBubble {clientId}";
            bubble = new Bubble { go = go, renderer = go.GetComponentInChildren<Renderer>() };
            bubbles[clientId] = bubble;
            return bubble;
        }

        static bool TryGetBody(int clientId, out Collider body) {
            body = null;
            foreach (var pair in LocalCharacter.Characters) {
                if (pair.Key == null || pair.Key.ClientId != clientId || pair.Value == null || pair.Value.IsDead) continue;
                return pair.Value.TryGetComponent(out body) && body.enabled;
            }
            return false;
        }

        static Vector3? CentreOf(int clientId) => TryGetBody(clientId, out Collider body) ? body.bounds.center : null;
    }
}
