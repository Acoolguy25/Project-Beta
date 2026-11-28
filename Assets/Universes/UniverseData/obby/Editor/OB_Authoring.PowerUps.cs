using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if !UNITY_SERVER
using Universes.UniverseData.obby.Client;
#endif

namespace Universes.UniverseData.obby.Editor {
    // Round 4: the "?" block piece, the power-up pill on the HUD and the bubble material drawn round racers.
    public static partial class OB_Authoring {
        /// <summary>How far (block sizes) and how long (s) a "?" block hops when bumped.</summary>
        const float PowerBlockHop = 0.2f, PowerBlockHopSeconds = 0.28f;

        /// <summary>
        /// "?" block: a unit cube (OB_Course scales it to the rules' block size), origin at its centre. The solid
        /// collider stays on the root; the drawn block and its question marks hop when bumped.
        /// </summary>
        static GameObject BuildPowerBlock(FontSet font) {
            var fresh = BlockMat("PowerBlock", OB_Palette.Gold, 0.05f, 0.3f, smoothness: 0.55f);
            var used = BlockMat("PowerBlockUsed", OB_Palette.WoodDark, 0.05f, smoothness: 0.2f);
            var root = new GameObject("PowerBlock");
            root.AddComponent<BoxCollider>().size = Vector3.one;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var body = Cube("Block", visual, Vector3.zero, Vector3.one, fresh, false);
            var marks = new GameObject[4];
            for (int side = 0; side < 4; side++) {
                var face = new GameObject($"Mark{side}").transform;
                face.SetParent(visual, false);
                face.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                var text = new GameObject("Question", typeof(TextMeshPro));
                text.transform.SetParent(face, false);
                text.transform.localPosition = new Vector3(0f, 0.02f, -0.505f);
                var tmp = text.GetComponent<TextMeshPro>();
                tmp.text = "?";
                tmp.font = font.asset;
                tmp.fontSharedMaterial = font.outline;
                tmp.fontSize = 9f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.color = OB_Palette.Paper;
                tmp.rectTransform.sizeDelta = Vector2.one;
                marks[side] = face.gameObject;
            }
            var view = root.AddComponent<OB_PowerBlock>();
            view.visual = visual;
            view.body = body.GetComponent<Renderer>();
            view.freshMaterial = fresh;
            view.usedMaterial = used;
            view.marks = marks;
            view.bumpHeight = PowerBlockHop;
            view.bumpSeconds = PowerBlockHopSeconds;
            return Save(root, "PowerBlock");
        }

        /// <summary>The bubble round a racer with a shield or a rush: a unit-diameter glowing sphere, no collider, no shadow.</summary>
        static GameObject BuildPowerBubble() {
            var material = Mat("PowerBubble", OB_Palette.Gravity, emission: 0.8f, alpha: 0.16f, smoothness: 0.95f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PowerBubble";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return OB_StablePrefab.Save(go, Root + "/Prefabs/PowerBubble.prefab");
        }

#if !UNITY_SERVER
        /// <summary>
        /// The power-up pill under the stage card (icon, name, seconds left and a draining bar), and the
        /// OB_PowerUps component that runs it, the bubbles and the local player's powers.
        /// </summary>
        static void AddPowerUps(GameObject root, Transform hud, FontSet bold, Color panel, float shadow, float top) {
            var powers = root.AddComponent<OB_PowerUps>();
            powers.bubblePrefab = BuildPowerBubble();
            powers.shieldIcon = SpriteAt(Icons + "icon_shield.png");
            powers.bootsIcon = SpriteAt(Icons + "icon_flight.png");
            powers.rushIcon = SpriteAt(Icons + "icon_bolt.png");

            Vector2 topLeft = new(0f, 1f);
            var pill = Card("PowerPill", hud, topLeft, topLeft, new Vector2(HudSideMargin, -top), new Vector2(330f, 52f), panel, 26f, shadow);
            powers.pillRect = pill;
            powers.pill = pill.gameObject.AddComponent<CanvasGroup>();
            powers.pill.interactable = powers.pill.blocksRaycasts = false;
            powers.pill.alpha = 0f;
            var body = pill.Find("Body");
            powers.icon = Icon("Power", body, "icon_shield", new Vector2(0f, 0.5f), new Vector2(30f, 2f), 30f, OB_Palette.Gravity);
            powers.label = Label(Stretch("Name", body, Vector2.zero, Vector2.one, new Vector2(56f, 8f), new Vector2(-14f, 0f)),
                bold, bold.shadow, 20, OB_Palette.Paper, TextAlignmentOptions.MidlineLeft);
            powers.label.textWrappingMode = TextWrappingModes.NoWrap;
            powers.label.characterSpacing = 2f;
            var track = Stretch("Timer", body, Vector2.zero, new Vector2(1f, 0f), new Vector2(56f, 8f), new Vector2(-16f, 14f));
            Panel(track, OB_Palette.InkSoft, 3f);
            var fill = Stretch("Fill", track, Vector2.zero, Vector2.one);
            powers.timerFill = fill;
            powers.timerFillImage = Panel(fill, OB_Palette.Gravity, 3f);
        }
#endif
    }
}
