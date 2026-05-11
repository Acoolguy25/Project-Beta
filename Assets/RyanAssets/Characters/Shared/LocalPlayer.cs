using UnityEngine;
using FishNet.Object;

namespace RyanAssets.Characters
{
    public class LocalPlayer : MonoBehaviour
    {
        public static LocalPlayer Instance { get; private set; }
        public static Transform Character;
        public InstantEvent<Transform> OnCharacterAdded;
        [SerializeField] private Transform CharacterControl;
        private void Awake()
        {
            Instance = this;
            Character = null;
            OnCharacterAdded = new();
        }
        public void SetCharacter(Transform NewCharacter)
        {
            Character = NewCharacter;
            OnCharacterAdded.Invoke(NewCharacter);
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
    }
}