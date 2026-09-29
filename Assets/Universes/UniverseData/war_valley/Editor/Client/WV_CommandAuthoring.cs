using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Client;
using Universes.UniverseData.war_valley.Shared;
using static RyanAssets.Client.ClientUI.Command.Editor.UIAuthoringKit;

namespace Universes.UniverseData.war_valley.Editor.Client {
    public static partial class WV_AuthoringHUD {
        static void ConfigureSimplifiedCommands(GameObject root, WV_HUD hud) {
            Transform card = root.transform.Find("CommandPanel");
            WV_CommandMenu menu = card.GetComponent<WV_CommandMenu>();
            Button[] existing = card.GetComponentsInChildren<Button>(true);
            Button Find(string name) => existing.First(button => button.name == name);
            Transform advanced = card.Find("AdvancedControls");
            if (advanced == null)
                advanced = Panel(card, "AdvancedControls", PanelFill).transform;
            Anchor(advanced, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                new Vector2(12f, 10f), new Vector2(696f, 130f));
            Button freeze = card.Find("Freeze")?.GetComponent<Button>();
            if (freeze == null)
                freeze = Button(card, "Freeze", "Freeze", Danger, 14f, out _);
            Button more = card.Find("More")?.GetComponent<Button>();
            if (more == null)
                more = Button(card, "More", "More", ButtonFill, 14f, out _);
            Button clear = card.Find("Clear")?.GetComponent<Button>();
            if (clear == null)
                clear = Button(card, "Clear", "Clear", ButtonFill, 14f, out _);
            Button[] primary = { Find("Order0"), Find("Order4"), freeze, Find("Select2"), clear, more };
            string[] labels = { "Advance", "Hold & Fight", "Freeze", "Select Army", "Clear", "More" };
            for (int i = 0; i < primary.Length; i++) {
                primary[i].transform.SetParent(card, false);
                primary[i].GetComponentInChildren<TextMeshProUGUI>(true).text = labels[i];
                PlaceTopLeft(primary[i], 12f + i * 117f, 32f, 111f, 40f);
            }
            Hover(primary[0], "Click a destination. Troops fight on the way and resume their advance afterward.", gameHelp: true);
            Hover(primary[1], "Stay here and shoot anything in range. Troops will not chase or retreat.", gameHelp: true);
            Hover(freeze, "Stop movement and weapons. Advance or Hold & Fight resumes combat.", gameHelp: true);
            Hover(clear, "Clear the selection so world clicks select again.", gameHelp: true);
            for (int i = 1; i <= 3; i++) {
                Button order = Find($"Order{i}");
                order.transform.SetParent(advanced, false);
                PlaceTopLeft(order, 12f + (i - 1) * 224f, 12f, 216f, 34f);
            }
            for (int i = 0; i < 2; i++) {
                Button select = Find($"Select{i}");
                select.transform.SetParent(advanced, false);
                PlaceTopLeft(select, 12f + i * 132f, 54f, 124f, 30f);
            }
            for (int i = 0; i < 4; i++) {
                Button group = Find($"Group{i + 1}");
                Button set = Find($"SetGroup{i + 1}");
                group.transform.SetParent(advanced, false);
                set.transform.SetParent(advanced, false);
                PlaceTopLeft(group, 300f + i * 96f, 54f, 86f, 30f);
                PlaceTopLeft(set, 300f + i * 96f, 90f, 86f, 26f);
            }
            PlaceTopLeft(card.Find("Status"), 12f, 96f, 690f, 28f);
            Wire(menu, ("freezeButton", freeze), ("moreButton", more), ("clearButton", clear),
                ("advancedControls", advanced.gameObject), ("controlsHint", root.transform.Find("HintLabel").gameObject));
            Wire(hud, ("troopRangeIndicator", AssetDatabase.LoadAssetAtPath<WV_RangeIndicator>(
                Root + "/Presentation/WV_AttackRange.prefab")));
            advanced.gameObject.SetActive(false);
        }
    }
}
