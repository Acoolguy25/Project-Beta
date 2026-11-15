using UnityEngine;

namespace RyanAssets.TweenService.TweenEasing {
    public class LinearEasing : EasingClass {
        public override float TransformValue(float percentage) {
            return percentage;
        }
    }
}