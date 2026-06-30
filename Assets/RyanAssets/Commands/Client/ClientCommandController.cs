using System.Collections;
using UnityEngine;
using RyanAssets.UI.Autocomplete;
using RyanAssets.Commands.Shared;
using RyanAssets.Shared.Player;

namespace RyanAssets.Commands.Client {
    public class ClientCommandController : AutocompleteUI {
        protected override void Start() {
            base.Start();
            SharedGlobalEvents.OnCommandsUpdated += UpdateCommands;
            UpdateCommands();
        }
        void UpdateCommands() {
            ClearPrefabs();
            foreach (var command in SharedCommands.AllGameCommands) {
                AddPrefab(new() { display = command.commandName });
            }
            Refresh();
        }
    }
}