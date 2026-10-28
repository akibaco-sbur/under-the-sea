using UnityEngine;

/// <summary>
/// Voice-command movement for the diver. MoveForward starts acceleration and
/// Stop requests a soft deceleration. Body lean follows current forward speed.
/// </summary>
[DefaultExecutionOrder(-10)]
[DisallowMultipleComponent]
public sealed class DiverMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private Transform movementRoot;
    [SerializeField] private Transform movementReference;
    [SerializeField, Min(0.01f)] private float maximumSpeed = 0.75f;
    [SerializeField, Min(0.01f)] private float acceleration = 0.55f;
    [SerializeField, Min(0.01f)] private float deceleration = 1.2f;

    [Header("Speed-based body lean")]
    [SerializeField] private Transform rootBone;
    [SerializeField] private Transform spine005Bone;
    [SerializeField, Range(-30f, 30f)] private float maximumRootLeanDegrees = 8f;
    [SerializeField, Range(0f, 2f)] private float spineCounterLeanMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float leanResponse = 3f;

    private Quaternion rootRestRotation;
    private Quaternion spineRestRotation;
    private float speed;
    private float currentLean;
    private bool moving;

    public float CurrentSpeed => speed;
    public bool IsMoving => moving;

    private void Awake()
    {
        if (movementRoot == null)
            movementRoot = transform;
        if (rootBone != null)
            rootRestRotation = rootBone.localRotation;
        if (spine005Bone != null)
            spineRestRotation = spine005Bone.localRotation;
    }

    private void Update()
    {
        float targetSpeed = moving ? maximumSpeed : 0f;
        float rate = moving ? acceleration : deceleration;
        speed = Mathf.MoveTowards(speed, targetSpeed, rate * Time.deltaTime);

        if (speed > 0f && movementRoot != null)
        {
            Vector3 forward = movementReference != null ? movementReference.forward : movementRoot.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f)
                movementRoot.position += forward.normalized * (speed * Time.deltaTime);
        }
    }

    private void LateUpdate()
    {
        float speedRatio = maximumSpeed > 0f ? Mathf.Clamp01(speed / maximumSpeed) : 0f;
        float targetLean = maximumRootLeanDegrees * speedRatio;
        currentLean = Mathf.MoveTowards(currentLean, targetLean, leanResponse * Time.deltaTime * Mathf.Max(1f, maximumRootLeanDegrees));

        if (rootBone != null)
            rootBone.localRotation = rootRestRotation * Quaternion.Euler(currentLean, 0f, 0f);
        if (spine005Bone != null)
            spine005Bone.localRotation = spineRestRotation * Quaternion.Euler(-currentLean * spineCounterLeanMultiplier, 0f, 0f);
    }

    /// <summary>Connect to the Vosk Move Forward UnityEvent.</summary>
    public void MoveForward()
    {
        moving = true;
    }

    /// <summary>Connect to the Vosk Stop UnityEvent.</summary>
    public void Stop()
    {
        moving = false;
    }

    public void ResetMovement()
    {
        moving = false;
        speed = 0f;
    }
}
