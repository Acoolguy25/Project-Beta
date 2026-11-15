using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using RyanAssets.Input;
using RyanAssets.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.PromptService {
    public enum PromptButton : sbyte {
        ErrorPromptId = -20,
        ErrorDuplicate = -10,
        Unknown = -1,
        Ok = 0,
        Yes = 1,
        Retry = 2,
        Cancel = 3,
        No = 4
    }

    public enum PromptId {
        None, // Will not display
        Protected, // Cannot be deleted
        Error, // Error state
        NetworkLoginAwait,
        LoginResponse,
        UsernameCheckAwait,
        UsernameChangeConfirm,
        UsernameChangeAwait,
        UsernameResponse,
        GamePageAwait,
        GamePageConfirm,
        PlayGameAwait,
        PlayGameConfirm,
        JoinGameAwait,
        JoinGameResponse,
        AuthenticationFail,
        ServerPromptBroadcast,
        LeaveGameConfirm,
        LeaveGameAwait,
        ResetCharacterConfirm,
        ResetCharAwait
    }

    public struct PromptData {
        public string title, description;
        public PromptId promptId;
        public PromptButton[] buttons;
        public UniTaskCompletionSource<PromptButton> response;
    }

    /// <summary>
    /// A queue of modal dialogs. Only the oldest prompt is shown; answering it shows the next one.
    /// Every button the dialog can show lives in the prefab (see <see cref="slots"/>) and is turned
    /// on per prompt, so the look is authored in the prefab, not in code.
    /// </summary>
    public class PromptManager : MonoBehaviour {
        public static PromptManager Instance;

        // Button presets
        public static readonly PromptButton[] ButtonPreset_YesNo = { PromptButton.Yes, PromptButton.No };
        public static readonly PromptButton[] ButtonPreset_OkCancel = { PromptButton.Ok, PromptButton.Cancel };
        public static readonly PromptButton[] ButtonPreset_RetryCancel = { PromptButton.Retry, PromptButton.Cancel };
        public static readonly PromptButton[] ButtonPreset_OkOnly = { PromptButton.Ok };
        public static readonly PromptButton[] ButtonPreset_CancelOnly = { PromptButton.Cancel };
        public static readonly PromptButton[] ButtonPreset_RetryOnly = { PromptButton.Retry };
        public static readonly PromptButton[] ButtonPreset_None = { };

        static readonly PromptButton[] ConfirmButtons = { PromptButton.Ok, PromptButton.Yes, PromptButton.Retry };
        static readonly PromptButton[] DenyButtons = { PromptButton.Cancel, PromptButton.No };

        [Serializable]
        public struct ButtonSlot {
            public PromptButton id;
            public Button button;
        }

        [SerializeField] CanvasGroupController content;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text descText;
        [Tooltip("Every button a prompt can show. Their order in the prefab is the order on screen.")]
        [SerializeField] ButtonSlot[] slots = Array.Empty<ButtonSlot>();
        [Tooltip("Shown instead of buttons while a prompt waits on something (no buttons).")]
        [SerializeField] GameObject waitingIndicator;
        [SerializeField, Min(0f)] float fadeDuration = 0.2f;

        readonly List<PromptData> queue = new();
        bool showing;

        void Awake() {
            Instance = this;
            foreach (ButtonSlot slot in slots) {
                PromptButton id = slot.id;
                slot.button.onClick.AddListener(() => PromptButtonPressed(id));
            }
            PromptControls.confirmEvent = PromptConfirmPressed;
            PromptControls.denyEvent = PromptDenyPressed;
            Render(0f);
        }

        void Render(float duration) {
            bool show = queue.Count > 0;
            if (show != showing)
                InputService.SetInputScreenActive(InputScreen.Prompt, show);
            showing = show;
            content.SetVisible(show, duration);
            if (!show)
                return;

            PromptData top = queue[0];
            titleText.text = top.title;
            descText.text = top.description;
            descText.gameObject.SetActive(!string.IsNullOrEmpty(top.description));
            foreach (ButtonSlot slot in slots)
                slot.button.gameObject.SetActive(top.buttons.Contains(slot.id));
            if (waitingIndicator != null)
                waitingIndicator.SetActive(top.buttons.Length == 0);
        }

        public void CompleteAction(int idx, PromptButton resp) {
            PromptData prompt = queue[idx];
            queue.RemoveAt(idx);
            if (!prompt.response.TrySetResult(resp))
                Debug.LogWarning($"Prompt \"{prompt.title}\" was already answered.");
            if (idx == 0) // the visible prompt
                Render(fadeDuration);
        }

        public bool CompleteAction(PromptId promptId, PromptButton resp) {
            int idx = queue.FindIndex(p => p.promptId == promptId);
            if (idx < 0)
                return false;
            CompleteAction(idx, resp);
            return true;
        }

        public void PromptButtonPressed(PromptButton btn) {
            if (showing)
                CompleteAction(0, btn);
        }

        public void PromptButtonPressed(int btn) => PromptButtonPressed((PromptButton)btn);

        /// <summary>Enter: the prompt's positive answer, if it has one.</summary>
        public void PromptConfirmPressed() => AnswerWithFirst(ConfirmButtons);

        /// <summary>Escape: the prompt's negative answer, if it has one.</summary>
        public void PromptDenyPressed() => AnswerWithFirst(DenyButtons);

        void AnswerWithFirst(PromptButton[] candidates) {
            if (!showing)
                return;
            PromptButton[] offered = queue[0].buttons;
            foreach (PromptButton button in candidates) {
                if (offered.Contains(button)) {
                    CompleteAction(0, button);
                    return;
                }
            }
        }

        public UniTask<PromptButton> PromptLocalUser(string title, string description, PromptId promptId, PromptButton[] buttons) {
            if (promptId == PromptId.None)
                return UniTask.FromResult(PromptButton.ErrorPromptId);
            if (promptId != PromptId.Protected && queue.Any(p => p.promptId == promptId))
                return UniTask.FromResult(PromptButton.ErrorDuplicate);

            var response = new UniTaskCompletionSource<PromptButton>();
            queue.Add(new PromptData {
                title = title,
                description = description,
                buttons = buttons,
                promptId = promptId,
                response = response
            });
            if (queue.Count == 1)
                Render(fadeDuration);
            return response.Task;
        }

        // Shorthands
        public static void PromptError(string title, Exception e) =>
            Instance.PromptLocalUser(title + " Error", e.Message, PromptId.Error, ButtonPreset_OkOnly);

        public static void PromptError(string title, string e) =>
            Instance.PromptLocalUser(title + " Error", e, PromptId.Error, ButtonPreset_OkOnly);

        public static void PromptWait(string title, string description, PromptId promptId) =>
            Instance.PromptLocalUser(title, description, promptId, ButtonPreset_None);

        public static UniTask<PromptButton> PromptCancelableWait(string title, string description, PromptId promptId) =>
            Instance.PromptLocalUser(title, description, promptId, ButtonPreset_CancelOnly);

        public static void PromptOk(string title, string description, PromptId promptId = PromptId.Protected) =>
            Instance.PromptLocalUser(title, description, promptId, ButtonPreset_OkOnly);

        public static bool PromptDelete(PromptId promptId) =>
            Instance.CompleteAction(promptId, PromptButton.Unknown);

        public static bool HasPrompt(PromptId promptId) =>
            Instance.queue.Any(p => p.promptId == promptId);
    }
}
