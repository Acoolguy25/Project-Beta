using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// The loadout card above the team card: the gun (G) and perk (H) you picked, how many of each your
    /// level has unlocked, and "next life" while the pick differs from what this life wears. G and H
    /// step forward, Shift+G and Shift+H back; a change says the pick's name and what it does. A key press
    /// asks the server (<see cref="CTF_LoadoutRequest"/>), which decides and answers with
    /// <see cref="CTF_LoadoutBroadcast"/>; this only draws the answer.
    /// </summary>
    public sealed class CTF_LoadoutHud : MonoBehaviour {
        public HudPopup card;
        public Image gunIcon, perkIcon;
        public TMP_Text gunName, gunNote, perkName, perkNote;
        public HudChip nextLife;
        [Tooltip("While this popup shows (the respawn card), a change is not announced in the centre: the card's \"Next life\" chip says it.")]
        public HudPopup quietWhileShown;
        [Tooltip("Icon per gun and per perk, in CTF_Weapon / CTF_Perk order (baked by the HUD authoring).")]
        public Sprite[] weaponIcons = new Sprite[0];
        public Sprite[] perkIcons = new Sprite[0];
        [SerializeField] InputAction cycleGunInput = new("CycleGun", InputActionType.Button, "<Keyboard>/g");
        [SerializeField] InputAction cyclePerkInput = new("CyclePerk", InputActionType.Button, "<Keyboard>/h");
        [Min(0f)] public float changePunch = 0.12f;
        [Tooltip("Centre notice for a change or a locked key: seconds up and size (1 = the countdown's size).")]
        [Min(0.2f)] public float noticeSeconds = 1.2f;
        [Range(0.1f, 1f)] public float noticeSize = 0.35f;
        public Color lockedNoteColor = new(0.62f, 0.66f, 0.72f);
        [Tooltip("The description line under a changed pick's name: its size (share of the name's) and the extra seconds it stays up.")]
        [Range(0.2f, 1f)] public float descriptionSize = 0.5f;
        [Min(0f)] public float descriptionExtraSeconds = 0.8f;

        ClientManager manager;
        CTF_LoadoutBroadcast shown;
        bool hasLoadout;

        void OnEnable() {
            cycleGunInput.Enable();
            cyclePerkInput.Enable();
        }

        void OnDisable() {
            cycleGunInput.Disable();
            cyclePerkInput.Disable();
        }

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
            }
            if (!hasLoadout || manager == null || !manager.Started) return;
            // The character map is off while typing in chat or a menu is open.
            if (InputService.Instance != null && !InputService.GetActionMapActive(RyanAssetsActionMap.Character)) return;
            int step = Back() ? -1 : 1;
            if (cycleGunInput.WasPressedThisFrame()) Ask(CTF_LoadoutSlot.Weapon, step);
            if (cyclePerkInput.WasPressedThisFrame()) Ask(CTF_LoadoutSlot.Perk, step);
        }

        /// <summary>Shift held: step back through what you own.</summary>
        static bool Back() {
            var keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        void Ask(CTF_LoadoutSlot slot, int step) {
            byte owned = slot == CTF_LoadoutSlot.Weapon ? shown.ownedWeapons : shown.ownedPerks;
            int count = slot == CTF_LoadoutSlot.Weapon ? CTF_Loadouts.Weapons.Length : CTF_Loadouts.Perks.Length;
            if (CTF_Loadouts.CountOwned(owned, count) <= 1) {
                // Nothing else to pick yet: say what unlocks next instead of a silent key.
                bool weapon = slot == CTF_LoadoutSlot.Weapon;
                string[] ids = weapon ? CTF_Loadouts.WeaponUnlockIds() : CTF_Loadouts.PerkUnlockIds();
                if (CTF_Retention.NextLocked(owned, ids, out int index, out int level)) {
                    string next = weapon ? CTF_Loadouts.Get((CTF_Weapon)index).title : CTF_Loadouts.Get((CTF_Perk)index).title;
                    ScreenFx.ShowText($"{next} unlocks at level {level}", lockedNoteColor, noticeSeconds, noticeSize);
                }
                GameSfx.Play(SfxCue.Error);
                Punch.Scale(card.transform, changePunch, noticeSeconds * 0.2f);
                return;
            }
            manager.Broadcast(new CTF_LoadoutRequest { slot = slot, step = step });
        }

        void OnLoadout(CTF_LoadoutBroadcast message, Channel channel) {
            shown = message;
            hasLoadout = true;
            var gun = CTF_Loadouts.Get(message.weapon);
            var perk = CTF_Loadouts.Get(message.perk);
            gunIcon.sprite = Pick(weaponIcons, (int)message.weapon);
            perkIcon.sprite = Pick(perkIcons, (int)message.perk);
            gunName.text = gun.title;
            perkName.text = perk.title;
            int guns = CTF_Loadouts.CountOwned(message.ownedWeapons, CTF_Loadouts.Weapons.Length);
            // "No perk" is always there and is not an unlock: count the real perks only.
            int perks = CTF_Loadouts.CountOwned(message.ownedPerks, CTF_Loadouts.Perks.Length) - 1;
            // Today's twist may lend a locked gun: say so, so nobody thinks it stays.
            gunNote.text = message.weaponLoaned ? $"{CTF_Daily.Title(message.twist)} loan" : $"{guns}/{CTF_Loadouts.Weapons.Length} unlocked";
            perkNote.text = $"{perks}/{CTF_Loadouts.Perks.Length - 1} unlocked";
            gunNote.color = perkNote.color = lockedNoteColor;
            bool pending = message.weapon != message.liveWeapon || message.perk != message.livePerk;
            nextLife.SetVisible(pending);
            card.Show();
            if (!message.changed) return;
            Punch.Scale((message.changedSlot == CTF_LoadoutSlot.Weapon ? gunIcon : perkIcon).transform, changePunch * 2f, noticeSeconds * 0.2f);
            GameSfx.Play(SfxCue.UiClick);
            bool weaponSlot = message.changedSlot == CTF_LoadoutSlot.Weapon;
            string what = weaponSlot ? gun.title : perk.title;
            string does = weaponSlot ? gun.description : perk.description;
            if (quietWhileShown != null && quietWhileShown.Visible) return;
            // The name, then what it does, smaller, so a pick is never blind (try-1 review).
            ScreenFx.ShowText(CTF_Loadouts.PickNotice(what, does, pending, descriptionSize), Color.white, noticeSeconds + descriptionExtraSeconds, noticeSize);
        }

        static Sprite Pick(Sprite[] icons, int i) => icons != null && i >= 0 && i < icons.Length ? icons[i] : null;
    }
}
