
using UnityEngine;
using UnityEngine.UI;
using RyanAssets.TweenService;
using RyanAssets.TweenService.TweenEasing;
using System;

namespace RyanAssets.TweenService.TweenComponents {
    public enum SpinDirection {
        Clockwise = 1,
        Counterclockwise = -1
    };
    [RequireComponent(typeof(Image))]
    public static class TweenImage {
        public static void SpinImage(Image image, float duration, float degrees = 360f, SpinDirection spinDirection = SpinDirection.Counterclockwise, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update, EasingClass easing = null) {
            Transform imageTransform = image.transform;
            float offset = imageTransform.rotation.eulerAngles.z;
            Action<float> onChange = (float percent) => {
                imageTransform.rotation = Quaternion.Euler(0f, 0f, offset + percent * degrees * (float)spinDirection);
            };
            TweenManager.Instance.RegisterTween(duration, onChange, delta, update, easing);
        }
        public static void ColorImage(Image image, float duration, Color targetColor, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update, EasingClass easing = null) {
            Color startColor = image.color;
            Action<float> onChange = (float percent) => {
                image.color = Color.Lerp(startColor, targetColor, percent);
            };
            TweenManager.Instance.RegisterTween(duration, onChange, delta, update, easing);
        }
    }
}