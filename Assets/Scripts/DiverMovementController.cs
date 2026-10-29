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

    [Header("Obstacle stop")]
    [SerializeField] private Transform obstacleRayOrigin;
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField, Min(0.05f)] private float obstacleRayHeight = 1f;
    [SerializeField, Min(0f)] private float obstacleClearance = 0.45f;

    [Header("Speed-based body lean")]
    [SerializeField] private Transform rootBone;
    [SerializeField] private Transform spine005Bone;
    [SerializeField] private DiverViewController viewController;
    [SerializeField, Range(-30f, 30f)] private float maximumRootLeanDegrees = 8f;
    [SerializeField, Range(0f, 2f)] private float spineCounterLeanMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float leanResponse = 3f;

    private float speed;
    private float currentLean;
    private float appliedRootLean;
    private float appliedSpineCounterLean;
    private bool moving;
    private readonly RaycastHit[] obstacleHits = new RaycastHit[16];

    public float CurrentSpeed => speed;
    public bool IsMoving => moving;

    private void Awake()
    {
        if (movementRoot == null)
            movementRoot = transform;
        if (viewController == null)
            viewController = GetComponent<DiverViewController>();
        if (viewController != null && spine005Bone != null)
            viewController.SetSpine005Bone(spine005Bone);
    }

    private void Update()
    {
        Vector3 forward = GetMovementDirection();
        if (moving && speed > 0f && HasObstacleAhead(forward))
        {
            moving = false;
            Debug.Log("[DiverMovement] Obstacle detected ahead. Slowing to a stop.", this);
        }

        float targetSpeed = moving ? maximumSpeed : 0f;
        float rate = moving ? acceleration : deceleration;
        speed = Mathf.MoveTowards(speed, targetSpeed, rate * Time.deltaTime);

        if (speed > 0f && movementRoot != null && forward.sqrMagnitude > 0.0001f)
        {
            movementRoot.position += forward * (speed * Time.deltaTime);
        }
    }

    private Vector3 GetMovementDirection()
    {
        if (movementRoot == null)
            return Vector3.zero;
        Vector3 forward = movementReference != null ? movementReference.forward : movementRoot.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
    }

    private bool HasObstacleAhead(Vector3 forward)
    {
        if (movementRoot == null || forward.sqrMagnitude < 0.0001f)
            return false;

        float safeDeceleration = Mathf.Max(0.01f, deceleration);
        float brakingDistance = speed * speed / (2f * safeDeceleration);
        float rayDistance = obstacleClearance + brakingDistance + speed * Time.deltaTime;
        Vector3 origin = obstacleRayOrigin != null
            ? obstacleRayOrigin.position
            : movementRoot.position + Vector3.up * obstacleRayHeight;
        Ray ray = new Ray(origin, forward);
        Debug.DrawRay(origin, forward * rayDistance, Color.red);

        int hitCount = Physics.RaycastNonAlloc(ray, obstacleHits, rayDistance, obstacleLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = obstacleHits[i].collider;
            if (hitCollider == null)
                continue;
            Transform hitTransform = hitCollider.transform;
            if (hitTransform == movementRoot || hitTransform.IsChildOf(movementRoot))
                continue;
            return true;
        }
        return false;
    }

    private void LateUpdate()
    {
        float speedRatio = maximumSpeed > 0f ? Mathf.Clamp01(speed / maximumSpeed) : 0f;
        float targetLean = maximumRootLeanDegrees * speedRatio;
        currentLean = Mathf.MoveTowards(currentLean, targetLean, leanResponse * Time.deltaTime * Mathf.Max(1f, maximumRootLeanDegrees));

        if (rootBone != null)
        {
            float leanDelta = currentLean - appliedRootLean;
            rootBone.localRotation *= Quaternion.Euler(leanDelta, 0f, 0f);
            appliedRootLean = currentLean;
        }

        float counterLean = -currentLean * spineCounterLeanMultiplier;
        if (viewController != null)
            viewController.SetMovementCounterLean(counterLean);
        else if (spine005Bone != null)
        {
            float counterLeanDelta = counterLean - appliedSpineCounterLean;
            spine005Bone.localRotation *= Quaternion.Euler(counterLeanDelta, 0f, 0f);
            appliedSpineCounterLean = counterLean;
        }
    }

    /// <summary>Connect to the Vosk Move Forward UnityEvent.</summary>
    public void MoveForward()
    {
        if (viewController != null)
            viewController.AlignBodyToView();
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
