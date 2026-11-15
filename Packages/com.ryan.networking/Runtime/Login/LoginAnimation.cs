using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Login {
    public class LoginAnimation: MonoBehaviour {
        [SerializeField] private Image targetImage;
        [SerializeField] private float tweenTime = 3f;

        [SerializeField]
        private Color[] colors =
        {
            Color.red,
            Color.yellow,
            Color.green,
            Color.cyan,
            Color.blue,
            Color.magenta
        };
        private int currentIndex, nextIndex;
        private float timer;

        void Start(){
            nextIndex = 1;
        }
        void Update(){
            timer += Time.deltaTime;

            targetImage.color = Color.Lerp(colors[currentIndex], colors[nextIndex], timer / tweenTime);

            if (timer >= tweenTime){
                timer = 0f;
                currentIndex = nextIndex;
                nextIndex = (currentIndex + 1) % colors.Length;
            }
        }
    }
}