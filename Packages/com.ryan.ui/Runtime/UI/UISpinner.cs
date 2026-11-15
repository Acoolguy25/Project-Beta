using UnityEngine;

namespace RyanAssets.UI {
    /// <summary>Spins its RectTransform while enabled, as a "working on it" indicator.</summary>
    public class UISpinner : MonoBehaviour {
        [Tooltip("Degrees per second; positive spins clockwise.")]
        [SerializeField] float speed = 300f;

        void Update() => transform.Rotate(0f, 0f, -speed * Time.unscaledDeltaTime);
    }
}
