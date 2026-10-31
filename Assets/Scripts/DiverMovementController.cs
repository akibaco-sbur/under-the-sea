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
    [SerializeField, InspectorName("体の中心を判定の起点にする")] private bool useBodyCenterForObstacles = true;
    [SerializeField, Min(0f), InspectorName("衝突判定の半径（m）")] private float obstacleProbeRadius = .25f;
    [SerializeField, InspectorName("ColliderのないRock・MossRockを補完")] private bool addMissingRockColliders = true;
    [Header("World border idle retreat")]
    [SerializeField, InspectorName("ボーダー停止後の自動撤退")] private bool autoRetreatAtBorder = true;
    [SerializeField, Min(1f), InspectorName("自動撤退までの秒数")] private float borderIdleRetreatSeconds = 15f;
    private readonly BorderIdleRetreat borderIdle = new BorderIdleRetreat();
    private bool borderBraking;
    public bool IsBorderIdleCountdownActive => autoRetreatAtBorder && borderIdle.Armed;
    public float BorderIdleSecondsRemaining => borderIdle.Remaining(Time.unscaledTimeAsDouble,borderIdleRetreatSeconds);

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
    private bool obstacleBraking;
    private float obstacleWarningUntil;
    public bool IsObstacleWarningActive => isActiveAndEnabled && (Time.unscaledTime < obstacleWarningUntil || IsBorderIdleCountdownActive);
    private bool pendingForward;
    private int pendingAlignmentVersion;
    private RaycastHit[] obstacleHits = new RaycastHit[32];
    private Collider[] obstacleOverlaps = new Collider[32];
    private SeaBedWorldBorder worldBorder;

    public float CurrentSpeed => speed;
    public float SpeedRatio => maximumSpeed > 0f ? Mathf.Clamp01(speed / maximumSpeed) : 0f;
    public bool IsMoving => moving;
    public bool IsAligningToMove => pendingForward;

    private void Awake()
    {
        if (movementRoot == null)
            movementRoot = transform;
        if (viewController == null)
            viewController = GetComponent<DiverViewController>();
        if (viewController != null)
        {
            Transform stableRoot = viewController.BodyRoot;
            if (movementRoot == rootBone || (movementRoot != stableRoot && movementRoot.IsChildOf(stableRoot)))
                movementRoot = stableRoot;
        }
        if (viewController != null && spine005Bone != null)
            viewController.SetSpine005Bone(spine005Bone);
        if (addMissingRockColliders && gameObject.scene.name == "Main")
            DiverRockColliders.Ensure(gameObject.scene);
    }

    private void Update()
    {
        if (!worldBorder) worldBorder = SeaBedWorldBorder.Ensure(gameObject.scene);
        if (worldBorder && worldBorder.IsReady && movementRoot)
            movementRoot.position = worldBorder.Constrain(movementRoot.position);
        if (DiverStartupGate.IsBlocked(gameObject.scene)) { ResetMovement(); borderBraking=false; borderIdle.Reset(); return; }
        if (viewController) viewController.SetMovingSteering(moving && !pendingForward);
        if (pendingForward)
        {
            speed = 0f;
            if (!viewController || !viewController.isActiveAndEnabled || viewController.AlignmentVersion != pendingAlignmentVersion)
            { pendingForward = false; moving = false; return; }
            if (viewController.IsAligningToView) return;
            pendingForward = false;
            moving = true;
        }
        Vector3 forward = GetMovementDirection();
        float dt = Mathf.Max(0f, Time.deltaTime);
        float nextSpeed = Mathf.MoveTowards(speed, moving ? maximumSpeed : 0f, (moving ? acceleration : deceleration) * dt);
        float allowedTravel = float.PositiveInfinity;
        float hitDistance = float.PositiveInfinity;
        bool blocked = (moving || speed > 0f) && TryObstacleAhead(forward, nextSpeed, dt, out hitDistance);
        bool borderIsClosest = false;
        if ((moving || speed > 0f) && worldBorder && worldBorder.IsReady && movementRoot)
        {
            float borderDistance = worldBorder.DistanceToEdge(movementRoot.position, forward);
            if (borderDistance <= DiverObstacleBraking.LookAhead(speed,nextSpeed,dt,deceleration,obstacleClearance))
            {
                blocked = true;
                borderIsClosest = borderDistance + obstacleClearance <= hitDistance + .001f;
                // DistanceToEdge already includes the border's inner safety margin.
                hitDistance = Mathf.Min(hitDistance,borderDistance + obstacleClearance);
            }
        }
        if (blocked)
        {
            borderBraking = borderIsClosest;
            allowedTravel = Mathf.Max(0f, hitDistance - obstacleClearance);
            obstacleBraking = true;
            obstacleWarningUntil = Time.unscaledTime + 3f;
            if (moving) Debug.Log("[DiverMovement] Obstacle ahead. Braking from body center; remaining=" + allowedTravel.ToString("F2") + " m.", this);
            moving = false;
        }
        if (viewController) viewController.SetMovingSteering(moving && !pendingForward);

        float targetSpeed = moving ? maximumSpeed : 0f;
        float rate = moving ? acceleration : deceleration;
        speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);
        if (obstacleBraking && speed > 0f) obstacleWarningUntil = Time.unscaledTime + 3f;

        if (speed > 0f && movementRoot != null && forward.sqrMagnitude > 0.0001f)
        {
            float requestedTravel = speed * dt;
            float travel = DiverObstacleBraking.ResolveTravel(requestedTravel, allowedTravel, out bool stoppedAtBoundary);
            movementRoot.position += forward * travel;
            if (worldBorder && worldBorder.IsReady)
                movementRoot.position = worldBorder.Constrain(movementRoot.position);
            if (stoppedAtBoundary)
            {
                speed = 0f;
                moving = false;
                if (viewController) viewController.SetMovingSteering(false);
            }
        }
    }

    private Vector3 GetMovementDirection()
    {
        if (movementRoot == null)
            return Vector3.zero;
        Vector3 forward = viewController != null ? viewController.BodyForward :
            movementReference != null ? movementReference.forward : movementRoot.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
    }

    private Vector3 ObstacleCenter => useBodyCenterForObstacles && viewController
        ? viewController.BodyCenter : obstacleRayOrigin ? obstacleRayOrigin.position
        : movementRoot.position + Vector3.up * obstacleRayHeight;

    private bool IsExternalObstacle(Collider collider)
    {
        if (!collider) return false;
        var owner = viewController ? viewController.BodyRoot : movementRoot;
        return collider.transform != owner && !collider.transform.IsChildOf(owner);
    }

    private bool TryObstacleAhead(Vector3 forward, float nextSpeed, float dt, out float distance)
    {
        distance = float.PositiveInfinity;
        if (movementRoot == null || forward.sqrMagnitude < 0.0001f)
            return false;
        float rayDistance = DiverObstacleBraking.LookAhead(speed, nextSpeed, dt, deceleration, obstacleClearance);
        Vector3 origin = ObstacleCenter;
        Ray ray = new Ray(origin, forward);
        // The center ray is supplemented by a small swept sphere, to avoid grazing a rock.
        // Overlap handles a probe already touching a collider, which casts alone can miss.
        if (obstacleProbeRadius > 0f)
        {
            int count;
            while (true)
            {
                count = Physics.OverlapSphereNonAlloc(origin, obstacleProbeRadius, obstacleOverlaps, obstacleLayers, QueryTriggerInteraction.Ignore);
                if(count < obstacleOverlaps.Length)break;
                if(obstacleOverlaps.Length>=1024){distance=0;return true;}
                System.Array.Resize(ref obstacleOverlaps,obstacleOverlaps.Length*2);
            }
            for(int i=0;i<count;i++)if(IsExternalObstacle(obstacleOverlaps[i])){distance=0;break;}
        }
        for(int pass=0;pass<(obstacleProbeRadius>0f?2:1);pass++)
        {
            int count;
            while(true)
            {
                count=pass==0 ? Physics.RaycastNonAlloc(ray,obstacleHits,rayDistance,obstacleLayers,QueryTriggerInteraction.Ignore)
                    : Physics.SphereCastNonAlloc(ray,obstacleProbeRadius,obstacleHits,rayDistance,obstacleLayers,QueryTriggerInteraction.Ignore);
                if(count<obstacleHits.Length)break;
                if(obstacleHits.Length>=1024){distance=0;return true;}
                System.Array.Resize(ref obstacleHits,obstacleHits.Length*2);
            }
            for(int i=0;i<count;i++)if(IsExternalObstacle(obstacleHits[i].collider))distance=Mathf.Min(distance,obstacleHits[i].distance);
        }
        bool found = !float.IsPositiveInfinity(distance);
        Debug.DrawRay(origin, forward * rayDistance, found ? Color.red : Color.green);
        return found;
    }

    private void LateUpdate()
    {
        UpdateBorderIdleRetreat();
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

    private void UpdateBorderIdleRetreat()
    {
        if (!autoRetreatAtBorder || DiverStartupGate.IsBlocked(gameObject.scene) || !movementRoot || !worldBorder || !worldBorder.IsReady)
        { borderIdle.Reset(); return; }
        Vector3 position = movementRoot.position;
        if (!borderIdle.Armed && borderBraking && speed <= .005f && !moving && !pendingForward)
            borderIdle.Arm(position.x,position.z,Time.unscaledTimeAsDouble);
        bool wasArmed = borderIdle.Armed;
        if (borderIdle.Tick(position.x,position.z,Time.unscaledTimeAsDouble,borderIdleRetreatSeconds))
        {
            Debug.Log("[DiverMovement] Stationary after world-border stop. Requesting retreat.",this);
            DiverRetreatController.Request(gameObject.scene);
            return;
        }
        if (wasArmed && !borderIdle.Armed) borderBraking=false;
    }

    /// <summary>Connect to the Vosk Move Forward UnityEvent.</summary>
    public void MoveForward()
    {
        if (DiverStartupGate.IsBlocked(gameObject.scene)) return;
        if (moving || pendingForward) return;
        obstacleBraking = false;
        obstacleWarningUntil = 0f;
        pendingForward = moving = false;
        speed = 0f;
        if (viewController != null && viewController.TryAlignBodyToView(out pendingAlignmentVersion))
            pendingForward = true;
        else Debug.LogWarning("[DiverMovement] Forward movement cancelled because body alignment is unavailable.", this);
    }

    /// <summary>Connect to the Vosk Stop UnityEvent.</summary>
    public void Stop()
    {
        if (viewController) viewController.SetMovingSteering(false);
        pendingForward = false;
        moving = false;
    }

    public void ResetMovement()
    {
        obstacleBraking = false;
        obstacleWarningUntil = 0f;
        if (viewController) viewController.SetMovingSteering(false);
        pendingForward = false;
        moving = false;
        speed = 0f;
    }

    private void OnDisable()
    {
        borderIdle.Reset();
        borderBraking=false;
        ResetMovement();
    }
}
