using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using RyanAssets.UI;
using Unity.VisualScripting;
using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace RyanAssets.Prompt {
    public enum PromptButton: sbyte {
        Unknown = -1,
        Ok = 0,
        Yes = 1,
        Cancel = 2,
        No = 3
    };
    public enum PromptId{
        TestId
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
        public static readonly PromptButton[] ButtonPreset_OkOnly = {PromptButton.Ok};

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
            PromptInProgress = PromptList.Count > 0;
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
                }
            }
        }
        public void CompleteAction(int idx, PromptButton resp){
            PromptData top_prompt = PromptList[idx];
            Debug.Assert(top_prompt.response.TrySetResult(resp), $"Failed to set prompt result for: ${top_prompt.title}");
            PromptList.RemoveAt(idx);
            if (idx == 0) // active index
                UpdateRenderer();
        }
        public int CompleteAction(PromptId promptId, PromptButton resp){
            for (int idx = 0; idx < PromptList.Count; idx++){
                PromptData prompt = PromptList[idx];
                if (prompt.promptId == promptId){
                    CompleteAction(idx, resp);
                    return 1;
                }
            }
            return 0;
        }
        public void PromptButtonPressed(PromptButton btn){
            CompleteAction(0, btn);
        }
        public void PromptButtonPressed(int btn){
            CompleteAction(0, (PromptButton)btn);
        }
        public Task<PromptButton> PromptLocalUser(string title, string description, PromptId promptId, PromptButton[] buttons){
            var promptResponse = new TaskCompletionSource<PromptButton>();
            PromptData newPrompt = new();
            newPrompt.title = title;
            newPrompt.description = description;
            newPrompt.buttons = buttons;
            newPrompt.promptId = promptId;
            newPrompt.response = promptResponse;
            PromptList.Add(newPrompt);
            if (!PromptInProgress)
                UpdateRenderer();
            return promptResponse.Task;
        }
        void Awake(){
            Instance = this;
            PromptList = new();
        }
        void Start(){
            UpdateRenderer(0f);
        }
    }
}