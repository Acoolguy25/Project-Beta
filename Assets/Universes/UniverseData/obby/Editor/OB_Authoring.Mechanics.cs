using RyanAssets.Characters.Shared;
using RyanAssets.Items.Platform;
using RyanAssets.Platformer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.obby.Editor {
    // Piece prefabs for the overhaul's mechanics. Each has its own colour (OB_Palette) so it reads at a
    // glance in every tier theme, and its origin and scaling follow what OB_Course.Spawn expects.
    public static partial class OB_Authoring {
        sealed class MechanicPrefabs {
            public GameObject beam, onOffBlock, onOffSwitchPad, onOffSwitchBlock, pendulum, crusher, laser, rollingLog, turntable;
            public GameObject cannon, mushroom, gravityFlipPad, gravityFlipOrb, risingLava, star, ice, wind;
            public Material onOffSolidA, onOffGhostA, onOffSolidB, onOffGhostB;
        }

        /// <summary>Star outline: points and the inner radius as a fraction of the outer one.</summary>
        const int StarPoints = 5;
        const float StarInner = 0.45f;
        const float StarSize = 1.3f, StarDepth = 0.3f;
        const string StarMeshPath = Pieces + "/StarMesh.asset";
        /// <summary>How far (m) and how long (s) a "!" block pops up when bumped from below.</summary>
        const float BlockBumpHeight = 0.35f, BlockBumpSeconds = 0.25f;
        /// <summary>The crusher face: brow tilt (degrees, inner ends down) and the teeth in its grin.</summary>
        const float CrusherBrowTilt = 18f;
        const int CrusherTeeth = 6;
        /// <summary>
        /// Upward pop (m/s) of a turntable post's shove. Kept low so a post at the fastest spin still shoves
        /// (knock under 6 m/s) instead of sending the robot limp.
        /// </summary>
        const float TurntablePostLift = 2f;

        static MechanicPrefabs BuildMechanics(FxPrefabs fx, Material lava) {
            var m = new MechanicPrefabs {
                onOffSolidA = BlockMat("OnOffRed", OB_Palette.OnOffRed, 0.06f, 0.35f),
                onOffGhostA = Mat("OnOffRedGhost", OB_Palette.OnOffRed, emission: 0.25f, alpha: 0.2f),
                onOffSolidB = BlockMat("OnOffBlue", OB_Palette.OnOffBlue, 0.06f, 0.35f),
                onOffGhostB = Mat("OnOffBlueGhost", OB_Palette.OnOffBlue, emission: 0.25f, alpha: 0.2f)
            };
            var stripes = Tex("Stripes");
            var beamMat = BlockMat("Beam", OB_Palette.Beam, 0.05f, detail: stripes, detailStrength: 0.18f);
            var steel = BlockMat("Steel", OB_Palette.Steel, 0.08f, smoothness: 0.6f);
            var hazardSteel = BlockMat("HazardSteel", OB_Palette.Steel, 0.05f, detail: stripes, detailStrength: 0.6f, smoothness: 0.55f);
            var crusherStone = BlockMat("CrusherStone", OB_Palette.CrusherStone, 0.12f, smoothness: 0.3f);
            var crusherMark = Mat("CrusherMark", OB_Palette.Warning, stripes, 0.35f, alpha: 0.75f);
            var ink = Mat("Ink", OB_Palette.Ink, smoothness: 0.5f);
            var white = Mat("MarkWhite", OB_Palette.Paper, emission: 0.6f);
            var laserOn = Mat("Laser", OB_Palette.Laser, emission: 3f, alpha: 0.65f);
            var laserWarn = Mat("LaserWarn", OB_Palette.Laser, emission: 1.2f, alpha: 0.3f);
            var laserOff = Mat("LaserOff", OB_Palette.Laser, emission: 0.3f, alpha: 0.06f);
            var wood = BlockMat("Log", OB_Palette.Wood, 0f, detail: stripes, detailStrength: 0.3f);
            var woodDark = BlockMat("LogEnd", OB_Palette.WoodDark, 0f);
            var disc = BlockMat("Turntable", OB_Palette.Turntable, 0.08f, 0.15f);
            var post = BlockMat("TurntablePost", OB_Palette.Warning, 0f, 0.3f);
            var cannonMat = Mat("Cannon", OB_Palette.Cannon, smoothness: 0.7f, metallic: 0.8f);
            var cannonRim = Mat("CannonRim", OB_Palette.Gold, emission: 0.4f, smoothness: 0.8f, metallic: 0.9f);
            var cap = BlockMat("MushroomCap", OB_Palette.MushroomCap, 0f, 0.15f);
            var spots = Mat("MushroomSpots", OB_Palette.MushroomSpots, emission: 0.3f);
            var stalk = BlockMat("MushroomStalk", OB_Palette.MushroomStalk, 0f);
            var flip = Mat("FlipArrows", OB_Palette.Flip, Tex("Arrows"), 1.4f);
            var flipGlow = Mat("FlipOrb", OB_Palette.Flip, emission: 2f, alpha: 0.55f);
            var starMat = Mat("Star", OB_Palette.Star, emission: 1.6f, smoothness: 0.85f, metallic: 0.6f);
            var iceMat = BlockMat("Ice", OB_Palette.Ice, 0.04f, 0.12f, smoothness: 0.95f, detail: Tex("Stripes"), detailStrength: 0.08f);
            var windStreak = FxMat("FxWindStreak", Tex("FxGlow"), false);

            m.beam = Save(Cube("Beam", null, Vector3.zero, Vector3.one, beamMat), "Beam");

            {   // ON/OFF block: a scaled box; colliders and material follow its channel.
                var go = Cube("OnOffBlock", null, Vector3.zero, Vector3.one, m.onOffSolidA);
                var block = go.AddComponent<OnOffBlock>();
                block.visuals = new[] { go.GetComponent<Renderer>() };
                block.solidMaterial = m.onOffSolidA;
                block.ghostMaterial = m.onOffGhostA;
                m.onOffBlock = Save(go, "OnOffBlock");
            }
            {   // Floor switch: origin on the surface; stepping on the button flips the channel.
                var root = new GameObject("OnOffSwitchPad");
                Cube("Base", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(2f, 0.24f, 2f), steel);
                var button = Cube("Button", root.transform, new Vector3(0f, 0.32f, 0f), new Vector3(1.4f, 0.16f, 1.4f), m.onOffSolidA, false);
                Trigger(root, new Vector3(0f, 1f, 0f), new Vector3(1.8f, 2f, 1.8f));
                var sw = root.AddComponent<OnOffSwitch>();
                sw.face = button.GetComponent<Renderer>();
                sw.pressVisual = button.transform;
                sw.unflippedMaterial = m.onOffSolidA;
                sw.flippedMaterial = m.onOffSolidB;
                m.onOffSwitchPad = Save(root, "OnOffSwitchPad");
            }
            {   // "!" block: origin at the block centre; the trigger under it catches a head bump from below.
                // The solid box stays put on the root; the drawn block (with its marks) pops up when bumped.
                var root = new GameObject("OnOffSwitchBlock");
                var solid = new GameObject("Solid");
                solid.transform.SetParent(root.transform, false);
                solid.AddComponent<BoxCollider>().size = Vector3.one * OB_CourseGenerator.SwitchBlockSize;
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                var block = Cube("Block", visual, Vector3.zero, Vector3.one * OB_CourseGenerator.SwitchBlockSize, m.onOffSolidA, false);
                // The "!" on each side: a bar and a dot.
                for (int side = 0; side < 4; side++) {
                    var face = new GameObject($"Mark{side}").transform;
                    face.SetParent(visual, false);
                    face.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                    float z = OB_CourseGenerator.SwitchBlockSize * 0.5f + 0.01f;
                    Cube("Bar", face, new Vector3(0f, 0.25f, z), new Vector3(0.36f, 0.95f, 0.04f), white, false);
                    Cube("Dot", face, new Vector3(0f, -0.62f, z), new Vector3(0.36f, 0.36f, 0.04f), white, false);
                }
                const float bumpDepth = 0.6f;
                Trigger(root, new Vector3(0f, -OB_CourseGenerator.SwitchBlockSize * 0.5f - bumpDepth * 0.5f, 0f),
                    new Vector3(OB_CourseGenerator.SwitchBlockSize * 0.9f, bumpDepth, OB_CourseGenerator.SwitchBlockSize * 0.9f));
                var sw = root.AddComponent<OnOffSwitch>();
                sw.face = block.GetComponent<Renderer>();
                sw.unflippedMaterial = m.onOffSolidA;
                sw.flippedMaterial = m.onOffSolidB;
                sw.pressVisual = visual;
                sw.pressDirection = Vector3.up;
                sw.pressDepth = BlockBumpHeight;
                sw.pressSeconds = BlockBumpSeconds;
                m.onOffSwitchBlock = Save(root, "OnOffSwitchBlock");
            }
            {   // Hammer: origin at the pivot; OB_Course stretches the arm and sizes the head.
                var root = new GameObject("Pendulum");
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var pendulum = root.AddComponent<Pendulum>();
                Primitive(PrimitiveType.Cylinder, "Pivot", root.transform, Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f), steel, false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pendulum.arm = Cube("Arm", root.transform, Vector3.down * 0.5f, new Vector3(0.3f, 1f, 0.3f), steel, false).transform;
                var head = Cube("Head", root.transform, Vector3.down, Vector3.one, hazardSteel);
                head.AddComponent<KnockbackSurface>();
                pendulum.head = head.transform;
                m.pendulum = Save(root, "Pendulum");
            }
            {   // Crusher: a scaled box at its raised pose. The crush zone hangs under the block, inset from its sides.
                var root = new GameObject("Crusher");
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var crusher = root.AddComponent<Crusher>();
                var block = Cube("Block", root.transform, Vector3.zero, Vector3.one, crusherStone);
                crusher.visual = block.transform;
                // Up against the camera (it passes just over the player's head) the block stops drawing.
                root.AddComponent<OB_NearCameraHide>().visual = block.transform;
                // A Thwomp face on the front and the back (local -Z faces the player coming along the
                // path): glaring eyes under angry brows, and a gritted grin. Unit space, the block's scale stretches it.
                for (int end = -1; end <= 1; end += 2) {
                    var face = new GameObject(end < 0 ? "Face" : "BackFace").transform;
                    face.SetParent(block.transform, false);
                    face.localRotation = Quaternion.Euler(0f, end < 0 ? 0f : 180f, 0f);
                    const float z = -0.505f;
                    for (int side = -1; side <= 1; side += 2) {
                        Cube($"Eye{side}", face, new Vector3(side * 0.2f, 0.1f, z), new Vector3(0.18f, 0.24f, 0.01f), white, false);
                        Cube($"Pupil{side}", face, new Vector3(side * 0.17f, 0.06f, z - 0.006f), new Vector3(0.07f, 0.12f, 0.01f), ink, false);
                        Cube($"Brow{side}", face, new Vector3(side * 0.2f, 0.3f, z - 0.006f), new Vector3(0.3f, 0.07f, 0.01f), ink, false)
                            .transform.localRotation = Quaternion.Euler(0f, 0f, side * CrusherBrowTilt);
                    }
                    Cube("Mouth", face, new Vector3(0f, -0.24f, z), new Vector3(0.56f, 0.16f, 0.01f), ink, false);
                    for (int tooth = 0; tooth < CrusherTeeth; tooth++) {
                        float x = (tooth - (CrusherTeeth - 1) * 0.5f) * (0.5f / CrusherTeeth);
                        bool upper = tooth % 2 == 0;
                        Cube($"Tooth{tooth}", face, new Vector3(x, upper ? -0.195f : -0.285f, z - 0.006f), new Vector3(0.5f / CrusherTeeth * 0.7f, 0.06f, 0.01f), white, false);
                    }
                }
                // Studs along the top edges, like a Thwomp's spikes.
                for (int stud = 0; stud < 4; stud++) {
                    float x = (stud - 1.5f) * 0.26f;
                    for (int end = -1; end <= 1; end += 2)
                        Cube($"Stud{stud}{(end < 0 ? "F" : "B")}", block.transform, new Vector3(x, 0.5f, end * 0.42f), new Vector3(0.12f, 0.08f, 0.12f), steel, false);
                }
                var zone = new GameObject("CrushZone");
                zone.transform.SetParent(root.transform, false);
                crusher.crushZone = Trigger(zone, new Vector3(0f, -0.55f, 0f), new Vector3(0.82f, 0.25f, 0.82f));
                zone.AddComponent<HazardVolume>();
                // Hazard stripes on the floor where it lands, held there while the block moves.
                var mark = Cube("LandingMark", root.transform, new Vector3(0f, -0.5f + 0.012f, 0f), new Vector3(0.76f, 0.006f, 0.9f), crusherMark, false);
                mark.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                crusher.landingMark = mark.transform;
                m.crusher = Save(root, "Crusher");
            }
            {   // Laser: a scaled trigger box that blinks (TimedToggleBlock) and hurts while on (HazardVolume).
                var go = Cube("Laser", null, Vector3.zero, Vector3.one, laserOn);
                go.GetComponent<BoxCollider>().isTrigger = true;
                var renderer = go.GetComponent<Renderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                var cycle = go.AddComponent<TimedToggleBlock>();
                cycle.visuals = new[] { renderer };
                cycle.solidMaterial = laserOn;
                cycle.ghostMaterial = laserOff;
                cycle.warnMaterial = laserWarn;
                cycle.warnSeconds = 0.15f;
                cycle.warnBeforeSolidSeconds = 0.45f;
                go.AddComponent<HazardVolume>().glowRenderer = renderer;
                m.laser = Save(go, "Laser");
            }
            {   // Rolling log: origin on the spin axis. The still collider is sized by OB_Course; only the log turns.
                var root = new GameObject("RollingLog");
                root.AddComponent<BoxCollider>();
                var log = root.AddComponent<RollingLog>();
                var spin = new GameObject("Spin").transform;
                spin.SetParent(root.transform, false);
                Primitive(PrimitiveType.Cylinder, "Log", spin, Vector3.zero, new Vector3(1f, 0.5f, 1f), wood, false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                for (int end = -1; end <= 1; end += 2)
                    Primitive(PrimitiveType.Cylinder, $"End{end}", spin, new Vector3(0f, 0f, end * 0.5f), new Vector3(1.02f, 0.01f, 1.02f), woodDark, false)
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                log.spinVisual = spin;
                m.rollingLog = Save(root, "RollingLog");
            }
            {   // Turntable: origin at the disc's top centre; spun by NetworkPlatform, riders carried round.
                var postRoot = new GameObject("TurntablePost");
                postRoot.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
                var postMesh = Primitive(PrimitiveType.Cylinder, "Post", postRoot.transform, Vector3.zero, new Vector3(1f, 0.5f, 1f), post, true);
                postMesh.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
                // Posts shove, they do not floor: the robot goes limp from a 6 m/s knock (CharacterParkour),
                // and the default 7 m/s lift alone would floor anyone a post touches, off the disc and out.
                var shove = new SerializedObject(postRoot.AddComponent<KnockbackSurface>());
                shove.FindProperty("lift").floatValue = TurntablePostLift;
                shove.ApplyModifiedPropertiesWithoutUndo();
                var postPrefab = Save(postRoot, "TurntablePost");

                var root = new GameObject("Turntable");
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                root.AddComponent<NetworkPlatform>();
                root.AddComponent<GroundMotionTransfer>().transferRotation = true;
                var table = root.AddComponent<Turntable>();
                var holder = new GameObject("Disc").transform;
                holder.SetParent(root.transform, false);
                var mesh = Primitive(PrimitiveType.Cylinder, "Mesh", holder, Vector3.zero, new Vector3(1f, 0.5f, 1f), disc, false);
                var collider = mesh.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
                collider.convex = true;
                // A stripe across the top shows the spin.
                Cube("Stripe", holder, new Vector3(0f, 0.505f, 0f), new Vector3(0.96f, 0.01f, 0.08f), white, false);
                table.disc = holder;
                table.postPrefab = postPrefab;
                table.postHeight = OB_PieceVolumes.TurntablePostHeight;
                m.turntable = Save(root, "Turntable");
            }
            {   // Cannon: origin on its surface. Walk into the barrel; LaunchPad aims the barrel along its shot.
                var root = new GameObject("Cannon");
                Cube("Base", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(2.6f, 0.24f, 2.6f), cannonMat);
                var barrel = new GameObject("Barrel").transform;
                barrel.SetParent(root.transform, false);
                barrel.localPosition = new Vector3(0f, 1.5f, 0f);
                Primitive(PrimitiveType.Cylinder, "Tube", barrel, new Vector3(0f, 0f, 0.4f), new Vector3(2f, 1.5f, 2f), cannonMat, false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Primitive(PrimitiveType.Cylinder, "Rim", barrel, new Vector3(0f, 0f, 1.9f), new Vector3(2.3f, 0.12f, 2.3f), cannonRim, false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 1.3f, 0f), new Vector3(1.8f, 2.4f, 1.8f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.aimVisual = barrel;
                pad.stretchAmount = 0f;
                pad.cooldown = 0.6f;
                m.cannon = Save(root, "Cannon");
            }
            {   // Mushroom: origin at the cap top, unit cap diameter (OB_Course scales it); the stalk hangs to the floor below.
                var root = new GameObject("Mushroom");
                var solid = root.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, -0.2f, 0f);
                solid.size = new Vector3(0.9f, 0.4f, 0.9f);
                Primitive(PrimitiveType.Sphere, "Cap", root.transform, new Vector3(0f, -0.18f, 0f), new Vector3(1.1f, 0.5f, 1.1f), cap, false);
                for (int i = 0; i < 5; i++) {
                    float angle = i * Mathf.PI * 2f / 5f;
                    Primitive(PrimitiveType.Sphere, $"Spot{i}", root.transform, new Vector3(Mathf.Cos(angle) * 0.3f, 0.02f, Mathf.Sin(angle) * 0.3f),
                        new Vector3(0.16f, 0.06f, 0.16f), spots, false);
                }
                var stem = new GameObject(OB_Course.MushroomStalk).transform;
                stem.SetParent(root.transform, false);
                stem.localScale = new Vector3(0.32f, 1f, 0.32f);
                Primitive(PrimitiveType.Cylinder, "Mesh", stem, Vector3.zero, new Vector3(1f, 0.5f, 1f), stalk, false);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 0.35f, 0f), new Vector3(0.95f, 0.7f, 0.95f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.stretchVisual = root.transform.Find("Cap");
                m.mushroom = Save(root, "Mushroom");
            }
            {   // Flip arrow: origin at the trigger centre, local up = gravity after the flip (OB_Course turns it);
                // the arrow plate lies on the surface the player stands on before the flip.
                var root = new GameObject("GravityFlipPad");
                Trigger(root, Vector3.zero, Vector3.one * 2f);
                var pad = root.AddComponent<GravityFlipPad>();
                Cube("Plate", root.transform, new Vector3(0f, -0.96f, 0f), new Vector3(2.2f, 0.06f, 1.8f), flip, false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var arrow = new GameObject("Arrow").transform;
                arrow.SetParent(root.transform, false);
                Cube("Chevron", arrow, Vector3.zero, new Vector3(1.2f, 1.2f, 0.06f), flip, false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                pad.arrow = arrow;
                m.gravityFlipPad = Save(root, "GravityFlipPad");
            }
            {   // Flip orb: a floating trigger, the same pad without a plate.
                var root = new GameObject("GravityFlipOrb");
                Trigger(root, Vector3.zero, Vector3.one * 2f);
                var pad = root.AddComponent<GravityFlipPad>();
                Primitive(PrimitiveType.Sphere, "Orb", root.transform, Vector3.zero, Vector3.one * 1.6f, flipGlow, false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var arrow = new GameObject("Arrow").transform;
                arrow.SetParent(root.transform, false);
                Cube("Chevron", arrow, Vector3.zero, new Vector3(0.9f, 0.9f, 0.05f), flip, false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                pad.arrow = arrow;
                m.gravityFlipOrb = Save(root, "GravityFlipOrb");
            }
            {   // Rising lava: a scaled box whose surface child rises during the local player's chase. No collider:
                // the server runs every player's chase.
                var root = new GameObject("RisingLava");
                var rising = root.AddComponent<RisingLava>();
                var surface = Cube("Surface", root.transform, Vector3.zero, Vector3.one, lava, false);
                surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                PrefabUtility.InstantiatePrefab(fx.lavaEmbers.gameObject, surface.transform);
                rising.surface = surface.transform;
                m.risingLava = Save(root, "RisingLava");
            }
            {   // Star: a spinning five-point star, whiter than a coin.
                var root = new GameObject("Star");
                var spin = new GameObject("Spin").transform;
                spin.SetParent(root.transform, false);
                spin.gameObject.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 120f, 0f);
                var shape = new GameObject("Shape", typeof(MeshFilter), typeof(MeshRenderer));
                shape.transform.SetParent(spin, false);
                shape.GetComponent<MeshFilter>().sharedMesh = StarMesh();
                var renderer = shape.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = starMat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                m.star = Save(root, "Star");
            }
            {   // Ice: a scaled box like a platform, glassy blue, with almost no grip.
                var go = Cube("Ice", null, Vector3.zero, Vector3.one, iceMat);
                go.AddComponent<IceSurface>();
                m.ice = Save(go, "Ice");
            }
            {   // Wind: an unscaled trigger box (OB_Course sizes it) with pale streaks flying down the wind.
                var root = new GameObject("Wind");
                Trigger(root, Vector3.zero, Vector3.one);
                var field = root.AddComponent<WindVolume>();
                var ps = NewParticles("Streaks", root.transform, windStreak, 1.5f, true, WindStreakMax);
                var main = ps.main;
                main.playOnAwake = true;   // client only: servers do not render
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startColor = OB_Palette.Wind.WithAlpha(0.95f);
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = Vector3.one;
                FadeOver(ps, Fade(Color.white, 0.15f, 0.55f));
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.15f;
                renderer.lengthScale = 4f;
                field.streaks = ps;
                m.wind = Save(root, "Wind");
            }
            return m;
        }

        /// <summary>Most wind streaks alive in one field.</summary>
        const int WindStreakMax = 160;

        /// <summary>The star prism (upright, facing ±Z), saved once and refilled in place so its GUID stays.</summary>
        static Mesh StarMesh() {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(StarMeshPath);
            if (mesh == null) {
                mesh = new Mesh { name = "Star" };
                AssetDatabase.CreateAsset(mesh, StarMeshPath);
            }
            mesh.Clear();
            int rim = StarPoints * 2;
            var outline = new Vector3[rim];
            for (int i = 0; i < rim; i++) {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / StarPoints;
                float r = StarSize * 0.5f * (i % 2 == 0 ? 1f : StarInner);
                outline[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
            }
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            // Front and back faces: a fan from the centre.
            for (int face = -1; face <= 1; face += 2) {
                int centre = vertices.Count;
                Vector3 offset = Vector3.forward * (face * StarDepth * 0.5f);
                vertices.Add(offset);
                for (int i = 0; i < rim; i++) vertices.Add(outline[i] + offset);
                for (int i = 0; i < rim; i++) {
                    int a = centre + 1 + i, b = centre + 1 + (i + 1) % rim;
                    if (face < 0) triangles.AddRange(new[] { centre, a, b });
                    else triangles.AddRange(new[] { centre, b, a });
                }
            }
            // Sides: one quad per edge, with its own vertices so the edges stay sharp.
            for (int i = 0; i < rim; i++) {
                Vector3 p = outline[i], q = outline[(i + 1) % rim];
                Vector3 back = Vector3.forward * (-StarDepth * 0.5f), front = Vector3.forward * (StarDepth * 0.5f);
                int start = vertices.Count;
                vertices.AddRange(new[] { p + back, q + back, q + front, p + front });
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void AssignMechanics(OB_Course course, MechanicPrefabs m) {
            course.beam = m.beam;
            course.onOffBlock = m.onOffBlock;
            course.onOffSwitchPad = m.onOffSwitchPad;
            course.onOffSwitchBlock = m.onOffSwitchBlock;
            course.pendulum = m.pendulum;
            course.crusher = m.crusher;
            course.laser = m.laser;
            course.rollingLog = m.rollingLog;
            course.turntable = m.turntable;
            course.cannon = m.cannon;
            course.mushroom = m.mushroom;
            course.gravityFlipPad = m.gravityFlipPad;
            course.gravityFlipOrb = m.gravityFlipOrb;
            course.risingLava = m.risingLava;
            course.star = m.star;
            course.ice = m.ice;
            course.wind = m.wind;
            course.onOffSolidA = m.onOffSolidA;
            course.onOffGhostA = m.onOffGhostA;
            course.onOffSolidB = m.onOffSolidB;
            course.onOffGhostB = m.onOffGhostB;
        }
    }
}
