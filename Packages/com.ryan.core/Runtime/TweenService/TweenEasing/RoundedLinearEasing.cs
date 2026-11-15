using UnityEngine;

namespace RyanAssets.TweenService.TweenEasing {

    public class RoundedLinearEasing : EasingClass {

        public override float TransformValue(float percentage) {
            return Mathf.SmoothStep(0f, 1f, percentage);
        }

    }

}