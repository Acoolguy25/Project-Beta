#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// The commander perk picker (P): every perk with its unlock level, the equipped ones marked. A
    /// click asks the server to equip or remove a perk (<see cref="LoadoutRequested"/>); the server checks
    /// it against the player's level and answers with a profile, which is what the panel then shows.
    /// Changes apply from the next match. Built by DI_Authoring; one row is cloned per perk at runtime.
    /// </summary>
    public sealed class DI_PerkPanel : MonoBehaviour {
        [Serializable]
        public struct PerkIcon {
            public string id;
            public Sprite sprite;
        }

        [SerializeField] HudPopup popup;
        [SerializeField] RectTransform frame;
        [SerializeField] DI_PerkRow rowTemplate;
        [SerializeField] TMP_Text slotsText;
        [Tooltip("Icon for each perk id, filled from DI_Perks by DI_Authoring.")]
        [SerializeField] List<PerkIcon> icons = new();

        [Header("Look")]
        [SerializeField] Color equippedColor = new(0.5f, 0.85f, 1f);
        [SerializeField] Color availableColor = new(0.85f, 0.88f, 0.92f);
        [SerializeField] Color lockedColor = new(0.55f, 0.58f, 0.64f);
        [SerializeField, Range(0f, 1f)] float lockedAlpha = 0.5f;
        [SerializeField, Min(0f)] float equipPunch = 0.08f;
        [SerializeField, Min(0.01f)] float equipPunchSeconds = 0.2f;

        [Header("Text")]
        [SerializeField] string equippedText = "Equipped";
        [SerializeField] string equipText = "Click to equip";
        [SerializeField] string swapText = "Click to swap in";
        [Tooltip("{0} is the unlock level.")]
        [SerializeField] string lockedFormat = "Level {0}";
        [Tooltip("{0} used slots, {1} slots, {2} the level the next slot unlocks at.")]
        [SerializeField] string slotsFormat = "{0} / {1} slots  ·  2nd slot at level {2}  ·  applies next match";
        [Tooltip("{0} used slots, {1} slots.")]
        [SerializeField] string fullSlotsFormat = "{0} / {1} slots  ·  applies next match";
        [SerializeField] string noSlotsText = "Reach level {0} to unlock your first perk.";
        [SerializeField] string unsavedText = "Progress is not saved this session: perks stay locked.";

        readonly List<DI_PerkRow> rows = new();
        // What the server saved, kept as sent: the profile can arrive before the level does, so the
        // equipped list is worked out from it again whenever the level changes.
        readonly List<string> savedLoadout = new();
        readonly List<string> equipped = new();
        int level = 1;
        bool saved = true;

        /// <summary>Raised with the whole loadout the player picked.</summary>
        public event Action<string[]> LoadoutRequested;

        public bool Visible => popup != null && popup.Visible;

        public IReadOnlyList<string> Equipped => equipped;

        void Awake() => BuildRows();

        void BuildRows() {
            if (rowTemplate == null || rows.Count > 0)
                return;
            rowTemplate.gameObject.SetActive(false);
            foreach (DI_PerkDefinition perk in DI_Perks.All) {
                DI_PerkRow row = Instantiate(rowTemplate, rowTemplate.transform.parent);
                row.gameObject.SetActive(true);
                row.Bind(perk, IconOf(perk.Id), OnRowClicked);
                rows.Add(row);
            }
            Refresh();
        }

        Sprite IconOf(string id) {
            foreach (PerkIcon entry in icons)
                if (entry.id == id)
                    return entry.sprite;
            return null;
        }

        public void SetVisible(bool visible) {
            if (popup == null)
                return;
            if (visible) {
                BuildRows();
                Refresh();
            }
            popup.SetVisible(visible);
        }

        public void Toggle() => SetVisible(!Visible);

        /// <summary>What the server last said: the saved loadout, the player's level and whether they are saved.</summary>
        public void SetProfile(IReadOnlyList<string> loadout, int playerLevel, bool isSaved) {
            level = Mathf.Max(1, playerLevel);
            saved = isSaved;
            savedLoadout.Clear();
            if (loadout != null)
                savedLoadout.AddRange(loadout);
            Refresh();
        }

        public void SetLevel(int playerLevel) {
            int next = Mathf.Max(1, playerLevel);
            if (next == level)
                return;
            level = next;
            Refresh();
        }

        public bool ContainsPointer(Vector2 screenPosition) =>
            Visible && frame != null && RectTransformUtility.RectangleContainsScreenPoint(frame, screenPosition, null);

        void OnRowClicked(string id) {
            if (!saved)
                return;
            List<string> next = DI_Perks.Toggle(equipped, id, level);
            savedLoadout.Clear();
            savedLoadout.AddRange(next);
            Refresh();
            foreach (DI_PerkRow row in rows)
                if (row.PerkId == id)
                    Punch.Scale(row.transform, equipPunch, equipPunchSeconds);
            LoadoutRequested?.Invoke(next.ToArray());
        }

        void Refresh() {
            equipped.Clear();
            equipped.AddRange(DI_Perks.Sanitize(savedLoadout, level));
            int slots = DI_Perks.SlotsAt(level);
            bool full = equipped.Count >= slots;
            foreach (DI_PerkRow row in rows) {
                DI_PerkDefinition perk = DI_Perks.Find(row.PerkId);
                if (perk == null)
                    continue;
                if (!saved || level < perk.UnlockLevel)
                    row.SetState(DI_PerkRowState.Locked, string.Format(lockedFormat, perk.UnlockLevel), lockedColor, equippedColor, lockedAlpha);
                else if (equipped.Contains(perk.Id))
                    row.SetState(DI_PerkRowState.Equipped, equippedText, equippedColor, equippedColor, lockedAlpha);
                else
                    row.SetState(DI_PerkRowState.Available, full ? swapText : equipText, availableColor, availableColor, lockedAlpha);
            }
            if (slotsText == null)
                return;
            if (!saved)
                slotsText.text = unsavedText;
            else if (slots == 0)
                slotsText.text = string.Format(noSlotsText, FirstUnlockLevel());
            else if (slots < DI_Perks.MaxSlots)
                slotsText.text = string.Format(slotsFormat, equipped.Count, slots, DI_Perks.SecondSlotLevel);
            else
                slotsText.text = string.Format(fullSlotsFormat, equipped.Count, slots);
        }

        static int FirstUnlockLevel() {
            int first = int.MaxValue;
            foreach (DI_PerkDefinition perk in DI_Perks.All)
                first = Mathf.Min(first, perk.UnlockLevel);
            return first;
        }
    }
}
#endif
