
using System;
using UnityEngine;
using RyanAssets.TweenService.TweenEasing;
using System.Collections.Generic;
using System.Linq;

namespace RyanAssets.TweenService {
    public enum TweenUpdateDelta {
        ScaledTime,
        RealTime
    };
    public enum TweenUpdateMethod {
        Update,
        FixedUpdate,
        LateUpdate,
        ManualUpdate
    };
    public class TweenTimer {
        public float transformedValue, realValue;
        public float start_timer, end_timer, cur_timer;
        public TweenUpdateDelta delta_method;
        public TweenUpdateMethod update_method;
        public EasingClass easingClass;
        public Action<float> onchange_event;
        public void UpdateTimer(float delta_time) {
            cur_timer = Mathf.Clamp(cur_timer + delta_time, 0f, end_timer);
            realValue = cur_timer / (end_timer - start_timer);
            transformedValue = Mathf.Clamp(easingClass.TransformValue(realValue), 0f, 1f);
            onchange_event?.Invoke(transformedValue);
        }
    };
    public class TweenManager : MonoBehaviour {
        public static TweenManager Instance { get; private set; }
        readonly List<TweenTimer> ActiveTweens = new();
        public void RegisterTween(float duration, Action<float> onchange_event, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update, EasingClass easing = null) {
            easing ??= new LinearEasing();
            TweenTimer newTimer = new() {
                delta_method = delta,
                update_method = update,
                easingClass = easing,
                onchange_event = onchange_event,
                start_timer = 0f,
                end_timer = duration,
                cur_timer = 0f
            };
            ActiveTweens.Add(newTimer);
        }
        void Awake() {
            Instance = this;
        }
        void UpdateAllTimers(TweenUpdateMethod method, float scaled_delta_time, float real_delta_time){
            // foreach (TweenTimer timer in ActiveTweens){
            for (int i = ActiveTweens.Count() - 1; i >= 0; i--){
                TweenTimer timer = ActiveTweens[i];
                if (timer.transformedValue == 1f)
                    ActiveTweens.RemoveAt(i);
                else if (timer.update_method == method)
                    timer.UpdateTimer(timer.delta_method == TweenUpdateDelta.ScaledTime? scaled_delta_time: real_delta_time);
            }
        }
        void Update() {
            UpdateAllTimers(TweenUpdateMethod.Update, Time.deltaTime, Time.unscaledDeltaTime);
        }
        void FixedUpdate(){
            UpdateAllTimers(TweenUpdateMethod.FixedUpdate, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        }
        void LateUpdate(){
            UpdateAllTimers(TweenUpdateMethod.LateUpdate, Time.deltaTime, Time.unscaledDeltaTime);
        }
    }
}