using UnityEngine;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>How a <see cref="CommandOption"/> can currently be used.</summary>
    public enum CommandOptionState : byte {
        /// <summary>Can be ordered right now.</summary>
        Available = 0,
        /// <summary>Unlocked, but the player cannot pay for it yet. Still clickable, so the refusal is explained.</summary>
        Unaffordable = 1,
        /// <summary>Gated behind something the player has not done yet, such as research.</summary>
        Locked = 2,
        /// <summary>Already under way - a research project in progress - and not orderable again.</summary>
        InProgress = 3,
        /// <summary>Finished for good, such as completed research.</summary>
        Done = 4
    }

    /// <summary>
    /// One thing a player can order from a building: a unit to train, a technology to research.
    /// <para>
    /// This is plain data so any game mode can fill it from its own rules. The grid never interprets
    /// it beyond drawing it; the mode maps <see cref="Id"/> back to its own meaning when the card is
    /// clicked.
    /// </para>
    /// </summary>
    public sealed class CommandOption {
        /// <summary>Game-defined identity, stable across refreshes and handed back on click.</summary>
        public int Id;
        public string Title;
        /// <summary>Short role line under the title, such as "Infantry" or "Aviation".</summary>
        public string Subtitle;
        /// <summary>Full detail shown when the card is hovered.</summary>
        public string Description;
        public Sprite Icon;
        /// <summary>Price in the mode's currency. Zero or less hides the price.</summary>
        public long Cost;
        /// <summary>How long the order takes. Zero or less hides the time.</summary>
        public float Seconds;
        public CommandOptionState State;
        /// <summary>Why the option is locked or what it is doing, shown across the card when set.</summary>
        public string StateText;
        /// <summary>0 to 1 progress drawn along the card's foot, or a negative value for none.</summary>
        public float Progress = -1f;
        /// <summary>How many of these are already queued. Zero hides the badge.</summary>
        public int Count;
    }

    /// <summary>One entry in a building's work queue.</summary>
    public struct CommandQueueEntry {
        public Sprite Icon;
        public string Title;
        /// <summary>0 to 1 for the entry in progress, or a negative value for one still waiting.</summary>
        public float Progress;
        /// <summary>Remaining time for the entry in progress. Empty for one still waiting.</summary>
        public string TimeText;
        /// <summary>Whether the local player may cancel this entry.</summary>
        public bool CanCancel;
        /// <summary>Colour of whoever the entry belongs to, drawn as a strip along the slot.</summary>
        public Color Accent;
        /// <summary>Hover detail for the slot.</summary>
        public string Tooltip;
    }

    /// <summary>The headline of whatever is selected: a building, a vehicle, a squad.</summary>
    public struct SelectionHeader {
        public string Title;
        /// <summary>Secondary line under the title. Supports TextMeshPro rich text, so an owner's name can carry their colour.</summary>
        public string Subtitle;
        public Sprite Icon;
        /// <summary>Colour strip identifying the owner. Fully transparent hides it.</summary>
        public Color Accent;
        public long Health;
        /// <summary>Zero or less hides the health bar.</summary>
        public long MaxHealth;
        /// <summary>Current activity, such as "Under construction - 12s".</summary>
        public string Status;
        /// <summary>0 to 1 progress for the status line, or a negative value for none.</summary>
        public float StatusProgress;
    }

    /// <summary>One labelled figure on the selection panel.</summary>
    public struct CommandStat {
        public string Label;
        public string Value;

        public CommandStat(string label, string value) {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// A button on the selection panel, such as Demolish or Set Rally Point.
    /// <para>
    /// An action with a <see cref="ConfirmLabel"/> needs a second click to go through, so the
    /// destructive ones cannot be triggered by a stray click.
    /// </para>
    /// </summary>
    public struct CommandAction {
        /// <summary>Game-defined identity handed back when the action fires.</summary>
        public int Id;
        public string Label;
        public bool Interactable;
        /// <summary>Hover detail. For a disabled action this should say why it is disabled.</summary>
        public string Tooltip;
        /// <summary>Drawn in the danger colour.</summary>
        public bool Destructive;
        /// <summary>When set, the first click shows this label and only a second click fires the action.</summary>
        public string ConfirmLabel;
    }

    /// <summary>
    /// Someone a <see cref="FundsTransferPanel"/> can send funds to. The mode decides who is
    /// eligible - an ally, a teammate - and hands back <see cref="Id"/> when the transfer is sent.
    /// </summary>
    public struct TransferRecipient {
        /// <summary>Game-defined identity, such as a client id.</summary>
        public int Id;
        public string Name;
        /// <summary>The recipient's colour, drawn beside their name.</summary>
        public Color Accent;
        /// <summary>A short second line, such as their current balance. Optional.</summary>
        public string Detail;
    }
}
