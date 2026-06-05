using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using RyanAssets.UI;
using Unity.VisualScripting;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using System.ComponentModel;
using RyanAssets.Input;

namespace RyanAssets.PromptService {
    public enum PromptButton: sbyte {
        Unknown = -1,
        Ok = 0,
        Yes = 1,
        Retry = 2,
        Cancel = 3,
        No = 4
    };
    public enum PromptId{
        Protected,
        Error,
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
        ServerPromptBroadcast,
        LeaveGameConfirm,
        LeaveGameAwait
    }
    public struct PromptData {
        public string title, description;
        public PromptId promptId;
        public PromptButton[] buttons;
        public TaskCompletionSource<PromptButton> response;
    };

    public class PromptManager: MonoBehaviour {
        // Instance Initalization
        public static PromptManager Instance;
        
        // Button Presets
        public static readonly PromptButton[] ButtonPreset_YesNo = {PromptButton.Yes, PromptButton.No};
        public static readonly PromptButton[] ButtonPreset_OkCancel = {PromptButton.Ok, PromptButton.Cancel};
        public static readonly PromptButton[] ButtonPreset_RetryCancel = {PromptButton.Retry, PromptButton.Cancel};
        public static readonly PromptButton[] ButtonPreset_OkOnly = {PromptButton.Ok};
        public static readonly PromptButton[] ButtonPreset_CancelOnly = {PromptButton.Cancel};
        public static readonly PromptButton[] ButtonPreset_RetryOnly = {PromptButton.Retry};
        public static readonly PromptButton[] ButtonPreset_None = {};

        List<PromptData> PromptList;
        // [SerializeField]
        // List<GameObject> PromptObjects;
        [SerializeField]
        CanvasGroupController canvasGroupUI;
        [SerializeField]
        TextMeshProUGUI titleText, descText;
        [SerializeField]
        Transform buttons;
        bool PromptInProgress;
        private void UpdateRenderer(float duration = 0.5f){
            bool NewPromptInProgress = PromptList.Count > 0;
            if (!PromptInProgress && NewPromptInProgress){
                InputService.FocusControls(InputControl.Prompt);
                InputService.UnlockControls(InputControl.Prompt);
            } else if (PromptInProgress && !NewPromptInProgress){
                InputService.UnfocusControls(InputControl.Prompt);
                InputService.LockControls(InputControl.Prompt);
            }
            PromptInProgress = NewPromptInProgress;
            canvasGroupUI.SetVisible(PromptInProgress, duration);
            if (PromptInProgress){
                PromptData TopPrompt = PromptList[0];
                titleText.text = TopPrompt.title;
                descText.text = TopPrompt.description;
                sbyte activeCnt = 0;
                for (sbyte btnIdx = 0; btnIdx < buttons.childCount; btnIdx++){
                    RectTransform btn = buttons.GetChild(btnIdx).GetComponent<RectTransform>();
                    bool visible = TopPrompt.buttons.Contains((PromptButton)btnIdx);
                    if (visible){
                        btn.anchorMin = new Vector2(((float) activeCnt) / TopPrompt.buttons.Count(), 0);
                        btn.anchorMax = new Vector2(((float) activeCnt + 1) / TopPrompt.buttons.Count(), 1);
                        activeCnt++;
                    }
                    btn.gameObject.SetActive(visible);
                }
            }
        }
        public void CompleteAction(int idx, PromptButton resp){
            PromptData top_prompt = PromptList[idx];
            PromptList.RemoveAt(idx);
            Debug.Assert(top_prompt.response.TrySetResult(resp), $"Failed to set prompt result for: ${top_prompt.title}");
            if (idx == 0) // active index
                UpdateRenderer();
        }
        public bool CompleteAction(PromptId promptId, PromptButton resp){
            for (int idx = 0; idx < PromptList.Count; idx++){
                PromptData prompt = PromptList[idx];
                if (prompt.promptId == promptId){
                    CompleteAction(idx, resp);
                    return true;
                }
            }
            return false;
        }
        public void PromptButtonPressed(PromptButton btn){
            if (PromptInProgress)
                CompleteAction(0, btn);
        }
        public void PromptButtonPressed(int btn){
            if (PromptInProgress)
                CompleteAction(0, (PromptButton)btn);
        }
        public void PromptConfirmPressed(){
            if (!PromptInProgress)
                return;

            PromptData prompt = PromptList[0];
            foreach (PromptButton button in new[]{PromptButton.Ok, PromptButton.Yes, PromptButton.Retry}){
                if (prompt.buttons.Contains(button))
                    CompleteAction(0, button);
            }
        }
        public void PromptDenyPressed(){
            if (!PromptInProgress)
                return;

            PromptData prompt = PromptList[0];
            foreach (PromptButton button in new[]{PromptButton.Cancel, PromptButton.No}){
                if (prompt.buttons.Contains(button))
                    CompleteAction(0, button);
            }
        }
        public Task<PromptButton> PromptLocalUser(string title, string description, PromptId promptId, PromptButton[] buttons){
            var promptResponse = new TaskCompletionSource<PromptButton>();
            PromptData newPrompt = new() {
                title = title,
                description = description,
                buttons = buttons,
                promptId = promptId,
                response = promptResponse
            };
            PromptList.Add(newPrompt);
            if (!PromptInProgress)
                UpdateRenderer();
            return promptResponse.Task;
        }
        void Awake(){
            Instance = this;
            PromptInProgress = false;
            PromptList = new();
            PromptControls.confirmEvent += PromptConfirmPressed;
            PromptControls.denyEvent += PromptDenyPressed;
            UpdateRenderer(0f);
            DontDestroyOnLoad(this);
        }


        // Useless helper functions
        public static void PromptError(string title, System.Exception e){
            Instance.PromptLocalUser(title + " Error", e.Message, PromptId.Error, ButtonPreset_OkOnly);
        }
        public static void PromptError(string title, string e){
            Instance.PromptLocalUser(title + " Error", e.ToString(), PromptId.Error, ButtonPreset_OkOnly);
        }
        public static void PromptWait(string title, string description, PromptId promptId){
            Instance.PromptLocalUser(title, description, promptId, ButtonPreset_None);
        }
        public static Task<PromptButton> PromptCancelableWait(string title, string description, PromptId promptId){
            return Instance.PromptLocalUser(title, description, promptId, ButtonPreset_CancelOnly);
        }
        public static void PromptOk(string title, string description, PromptId promptId = PromptId.Protected){
            Instance.PromptLocalUser(title, description, promptId, ButtonPreset_OkOnly);
        }
        public static bool PromptDelete(PromptId promptId){
            return Instance.CompleteAction(promptId, PromptButton.Unknown);
        }
    }
}