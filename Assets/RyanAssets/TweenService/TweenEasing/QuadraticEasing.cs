using UnityEngine;

namespace RyanAssets.TweenService.TweenEasing {

    public class QuadraticEasing : EasingClass {

        public override float TransformValue(float percentage) {
            return percentage < 0.5f
                ? 2f * percentage * percentage
                : 1f - Mathf.Pow(-2f * percentage + 2f, 2f) / 2f;
        }

    }

}