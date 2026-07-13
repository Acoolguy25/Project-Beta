using RyanAssets.TweenService.TweenEasing;
using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.TweenService.TweenComponents {
    public static class TweenRectTransform {
        public static void AnchorTween(RectTransform rect, float duration, Vector2 targetAnchorMin, Vector2 targetAnchorMax, bool affectPosition = false, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update, EasingClass easing = null) {
            Vector2 startAnchorMin = rect.anchorMin;
            Vector2 startAnchorMax = rect.anchorMax;

            RectTransform parentRect = rect.parent as RectTransform;
            Vector2 parentSize = parentRect != null ? parentRect.rect.size : Vector2.zero;

            Action<float> onChange = (float percent) => {
                Vector2 currentAnchorMin = Vector2.Lerp(startAnchorMin, targetAnchorMin, percent);
                Vector2 currentAnchorMax = Vector2.Lerp(startAnchorMax, targetAnchorMax, percent);

                if (affectPosition && parentRect != null) {
                    Vector2 anchorMinDelta = currentAnchorMin - rect.anchorMin;
                    Vector2 anchorMaxDelta = currentAnchorMax - rect.anchorMax;
                    Vector2 avgAnchorDelta = (anchorMinDelta + anchorMaxDelta) * 0.5f;
                    Vector2 posOffset = new Vector2(avgAnchorDelta.x * parentSize.x, avgAnchorDelta.y * parentSize.y);
                    rect.anchoredPosition -= posOffset;
                }

                rect.anchorMin = currentAnchorMin;
                rect.anchorMax = currentAnchorMax;
            };

            TweenManager.Instance.RegisterTween(duration, onChange, delta, update, easing, rect.gameObject);
        }

        // Pure anchoredPosition tween, no anchor involvement.
        public static void PositionTween(RectTransform rect, float duration, Vector2 targetPosition, TweenUpdateDelta delta = TweenUpdateDelta.RealTime, TweenUpdateMethod update = TweenUpdateMethod.Update, EasingClass easing = null) {
            Vector2 startPosition = rect.anchoredPosition;

            Action<float> onChange = (float percent) => {
                rect.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, percent);
            };

            TweenManager.Instance.RegisterTween(duration, onChange, delta, update, easing, rect.gameObject);
        }
    }
}