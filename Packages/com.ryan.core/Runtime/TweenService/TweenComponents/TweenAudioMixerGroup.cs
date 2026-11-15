using RyanAssets.TweenService.TweenEasing;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace RyanAssets.TweenService.TweenComponents {
    public static class TweenAudioMixerGroup {
        public static void FadeMixerVolume(AudioMixer audioMixer, string parameter, float targetDb, float duration, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update,
            EasingClass easing = null,
            object owner = null) {
            audioMixer.GetFloat(parameter, out float startDb);

            Action<float> onChange = (float percent) => {
                audioMixer.SetFloat(parameter, Mathf.Lerp(startDb, targetDb, percent));
            };

            TweenManager.Instance.RegisterTween(
                duration,
                onChange,
                delta,
                update,
                easing,
                owner
            );
        }
    }
}