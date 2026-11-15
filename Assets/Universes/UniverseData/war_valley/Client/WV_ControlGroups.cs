using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Selections parked on F1-F4 and recalled with one key.
    /// <para>
    /// The number row belongs to the toolbar, so control groups live on the function row rather than
    /// on the 1-9 an RTS would normally use. Four fits on F1-F4, which is as far as the function row
    /// can be used without running into keys the browser and the Editor take.
    /// </para>
    /// <para>
    /// A group holds the things themselves rather than their ids, so recalling one costs no lookup;
    /// members that died in the meantime are dropped on recall rather than tracked as it happens, so
    /// a group never has to be maintained to stay correct.
    /// </para>
    /// <para>Every method returns the hint-line message describing what happened.</para>
    /// </summary>
    public sealed class WV_ControlGroups {
        public const int Count = 4;

        /// <summary>One parked selection. Kept as a class so an empty slot is simply null.</summary>
        sealed class Group {
            public readonly List<WV_Unit> Units = new();
            public readonly List<GameCharacter> Troops = new();
        }

        readonly Group[] groups = new Group[Count];
        readonly WV_CommanderSelection selection;

        public WV_ControlGroups(WV_CommanderSelection selection) {
            this.selection = selection;
        }

        public static ButtonControl Key(Keyboard keyboard, int index) => index switch {
            0 => keyboard.f1Key,
            1 => keyboard.f2Key,
            2 => keyboard.f3Key,
            _ => keyboard.f4Key
        };

        /// <summary>Ctrl+F#: parks the current selection on a group key, replacing whatever was there.</summary>
        public string Bind(int index) {
            if (selection.Count == 0) {
                // Binding nothing is how a group is cleared, which is worth saying out loud rather
                // than leaving the player unsure whether the key registered.
                groups[index] = null;
                return $"Group F{index + 1} cleared";
            }

            var group = new Group();
            group.Units.AddRange(selection.Units);
            group.Troops.AddRange(selection.Troops);
            groups[index] = group;
            return $"Group F{index + 1} set - {selection.Count} selected";
        }

        /// <summary>Alt+F#: adds the current selection to a group, creating it if it was empty.</summary>
        public string Add(int index) {
            if (selection.Count == 0)
                return "Select something to add to the group";

            Group group = groups[index] ??= new Group();
            foreach (WV_Unit unit in selection.Units) {
                if (!group.Units.Contains(unit))
                    group.Units.Add(unit);
            }
            foreach (GameCharacter troop in selection.Troops) {
                if (!group.Troops.Contains(troop))
                    group.Troops.Add(troop);
            }
            return $"Added to group F{index + 1} - {group.Units.Count + group.Troops.Count} in group";
        }

        /// <summary>
        /// F#: replaces the selection with a parked group's surviving members. <paramref name="recalled"/>
        /// is false when the group was empty or wiped out, leaving the selection empty or unchanged.
        /// </summary>
        public string Recall(int index, out bool recalled) {
            recalled = false;
            Group group = groups[index];
            if (group == null)
                return $"Group F{index + 1} is empty";

            selection.Clear();
            foreach (WV_Unit unit in group.Units) {
                if (unit != null && unit.IsSpawned && !unit.IsDead)
                    selection.Add(unit);
            }
            foreach (GameCharacter troop in group.Troops) {
                if (selection.IsCommandableTroop(troop))
                    selection.Add(troop);
            }

            if (selection.Count == 0) {
                groups[index] = null;
                return $"Group F{index + 1} was wiped out";
            }

            recalled = true;
            return $"Group F{index + 1} - {selection.Count} selected";
        }
    }
}
