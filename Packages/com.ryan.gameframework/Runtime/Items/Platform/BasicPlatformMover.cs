using UnityEngine;

public class BasicPlatformMover : MonoBehaviour
{
    private Rigidbody rb;

    [SerializeField] private float speed = 2f;
    [SerializeField] private Vector3 rotate;

    private void Awake() {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate() {
        float direction = Time.time % 20f < 10f ? 1f : -1f;

        Vector3 targetPosition = rb.position + Vector3.forward * direction * speed * Time.fixedDeltaTime;
        Quaternion targetRotation = rb.rotation * Quaternion.Euler(rotate * Time.fixedDeltaTime);
        rb.MovePosition(targetPosition);
        rb.MoveRotation(targetRotation);
    }
}
