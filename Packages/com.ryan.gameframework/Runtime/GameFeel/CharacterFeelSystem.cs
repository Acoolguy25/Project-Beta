using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Character feel for every character this client sees: a jump sound and puff on take-off, a
    /// squash and dust on landing, and dust from the feet while running. Footstep and landing sounds
    /// themselves are already played by <see cref="CharacterAnimator"/> from the animation events.
    /// <para>
    /// Everything is measured along the character's own up (its transform, which the owner turns to
    /// match <see cref="CharacterGravity"/> and the network syncs), so walls and ceilings work.
    /// Grounded comes from the synced Animator parameter, so remote characters behave the same.
    /// </para>
    /// </summary>
    internal sealed class CharacterFeelSystem {
        sealed class Tracked {
            public GameCharacter character;
            public Transform root;
            public Transform model;
            public Animator animator;
            public CharacterAnimator events;
            public System.Action onFootstep;
            public bool hasGroundedParameter;
            public bool wasGrounded;
            public bool hasPosition;
            public Vector3 lastPosition;
            public float groundSpeed;
            public float peakFallSpeed;
            public bool near;
        }

        static readonly int GroundedId = Animator.StringToHash("Grounded");
        // Movement in one frame beyond this (m) is a teleport or respawn, not motion.
        const float TeleportDistance = 25f;
        // How fast the measured ground speed follows the real one (1/s).
        const float SpeedSmoothing = 12f;

        readonly GameFeelSettings.CharacterFeelTuning tuning;
        readonly GameFeelRunner runner;
        readonly List<Tracked> tracked = new();
        readonly bool subscribed;

        public CharacterFeelSystem(GameFeelSettings.CharacterFeelTuning tuning, GameFeelRunner runner) {
            this.tuning = tuning;
            this.runner = runner;
            if (!tuning.enabled)
                return;
            GameCharacter.GameCharacterAdded += Add;
            GameCharacter.GameCharacterRemoved += Remove;
            subscribed = true;
            // Characters that spawned before the kit started.
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values)
                foreach (GameCharacter character in team)
                    Add(character);
        }

        public void Dispose() {
            if (subscribed) {
                GameCharacter.GameCharacterAdded -= Add;
                GameCharacter.GameCharacterRemoved -= Remove;
            }
            for (int i = tracked.Count - 1; i >= 0; i--)
                Unbind(tracked[i]);
            tracked.Clear();
        }

        void Add(GameCharacter character) {
            if (character == null)
                return;
            for (int i = 0; i < tracked.Count; i++)
                if (tracked[i].character == character)
                    return;
            var entry = new Tracked {
                character = character,
                root = character.transform,
                animator = character.GetComponent<Animator>(),
                events = character.GetComponent<CharacterAnimator>(),
                model = FindModel(character.transform),
            };
            if (entry.animator != null)
                foreach (AnimatorControllerParameter parameter in entry.animator.parameters)
                    if (parameter.nameHash == GroundedId && parameter.type == AnimatorControllerParameterType.Bool)
                        entry.hasGroundedParameter = true;
            entry.wasGrounded = true;
            if (entry.events != null) {
                entry.onFootstep = () => OnFootstep(entry);
                entry.events.Footstep += entry.onFootstep;
            }
            tracked.Add(entry);
        }

        void Remove(GameCharacter character) {
            for (int i = tracked.Count - 1; i >= 0; i--) {
                if (tracked[i].character != character)
                    continue;
                Unbind(tracked[i]);
                tracked.RemoveAt(i);
            }
        }

        void Unbind(Tracked entry) {
            if (entry.events != null && entry.onFootstep != null)
                entry.events.Footstep -= entry.onFootstep;
            if (entry.model != null)
                runner.Punches.Cancel(entry.model);
        }

        /// <summary>
        /// The child of the character root that holds the skeleton (the robot's "KyleRobot"): scaling
        /// it squashes the skinned mesh without touching the root, its collider or the synced transform.
        /// </summary>
        static Transform FindModel(Transform root) {
            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform bone = skin != null && skin.rootBone != null ? skin.rootBone : null;
            if (bone == null)
                return null;
            while (bone.parent != null && bone.parent != root)
                bone = bone.parent;
            return bone.parent == root ? bone : null;
        }

        public void LateUpdate(Camera camera) {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            Vector3 viewPosition = camera != null ? camera.transform.position : Vector3.zero;
            float maxDistanceSqr = tuning.maxDistance * tuning.maxDistance;
            for (int i = tracked.Count - 1; i >= 0; i--) {
                Tracked entry = tracked[i];
                if (entry.character == null) {
                    Unbind(entry);
                    tracked.RemoveAt(i);
                    continue;
                }
                Step(entry, dt, camera != null && (entry.root.position - viewPosition).sqrMagnitude <= maxDistanceSqr);
            }
        }

        void Step(Tracked entry, float dt, bool near) {
            entry.near = near;
            Vector3 position = entry.root.position;
            Vector3 up = entry.root.up;
            if (!entry.hasPosition || (position - entry.lastPosition).sqrMagnitude > TeleportDistance * TeleportDistance) {
                entry.hasPosition = true;
                entry.lastPosition = position;
                entry.peakFallSpeed = 0f;
                return;
            }
            Vector3 velocity = (position - entry.lastPosition) / dt;
            entry.lastPosition = position;
            float along = Vector3.Dot(velocity, up);
            float ground = (velocity - up * along).magnitude;
            entry.groundSpeed = Mathf.Lerp(entry.groundSpeed, ground, 1f - Mathf.Exp(-SpeedSmoothing * dt));

            if (!entry.hasGroundedParameter || entry.character.IsDead)
                return;
            bool grounded = entry.animator.GetBool(GroundedId);
            if (!grounded)
                entry.peakFallSpeed = Mathf.Max(entry.peakFallSpeed, -along);

            if (near && entry.wasGrounded && !grounded && along >= tuning.jumpSpeed)
                OnJump(entry, position, up);
            if (near && !entry.wasGrounded && grounded && entry.peakFallSpeed >= tuning.landSpeed)
                OnLand(entry, position, up, entry.peakFallSpeed);
            if (grounded)
                entry.peakFallSpeed = 0f;
            entry.wasGrounded = grounded;
        }

        float BodyScale(Tracked entry) => Mathf.Max(0.01f, entry.root.lossyScale.y);

        void OnJump(Tracked entry, Vector3 position, Vector3 up) {
            if (tuning.jumpSound)
                GameSfx.PlayAt(SfxCue.Jump, position);
            GameFx.Play(FxKind.DustPuff, position, up, tuning.dustColor, tuning.footDustScale * BodyScale(entry));
        }

        void OnLand(Tracked entry, Vector3 position, Vector3 up, float speed) {
            float impact = Mathf.InverseLerp(tuning.landSpeed, tuning.hardLandSpeed, speed);
            if (entry.model != null)
                runner.Punches.Add(entry.model, PunchAnimator.Mode.Squash, Mathf.Lerp(tuning.minSquash, tuning.maxSquash, impact), tuning.squashSeconds);
            GameFx.Play(FxKind.DustPuff, position, up, tuning.dustColor, tuning.footDustScale * BodyScale(entry) * Mathf.Lerp(tuning.landDustScale.x, tuning.landDustScale.y, impact));
            if (tuning.landSound)
                GameSfx.PlayAt(SfxCue.Land, position, Mathf.Lerp(tuning.landVolume.x, tuning.landVolume.y, impact));
        }

        void OnFootstep(Tracked entry) {
            if (!entry.near || entry.groundSpeed < tuning.runDustSpeed || entry.character == null)
                return;
            GameFx.Play(FxKind.DustPuff, entry.root.position, entry.root.up, tuning.dustColor, tuning.footDustScale * BodyScale(entry));
        }
    }
}
