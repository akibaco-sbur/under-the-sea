using UnityEngine;

/// <summary>
/// Drives the diver's view from the face bone and keeps the separate
/// billboard-processing camera positioned at spine.006.
/// Voice recognition can call the public Look*/TurnAround methods.
/// </summary>
[DisallowMultipleComponent]
public sealed class DiverViewController : MonoBehaviour
{
    [Header("Rig references")]
    [SerializeField] private Transform bodyRoot;
    [SerializeField] private Transform faceBone;
    [SerializeField] private Transform spine005Bone;
    [SerializeField] private Transform spine006Bone;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Camera billboardCamera;
    public Camera ViewCamera => viewCamera;
    public Transform BodyRoot => ResolveBodyRoot();
    public Vector3 BodyCenter => spine005Bone ? spine005Bone.position : BodyRoot.position;
    public Vector3 BodyForward => bodyRoot ? Vector3.ProjectOnPlane(bodyRoot.rotation * bodyForwardInRoot, Vector3.up).normalized : transform.forward;
    public bool IsAligningToView => aligningToView;
    public int AlignmentVersion => alignmentVersion;
    public bool IsFeedingControlLocked { get; private set; }
    public void SetFeedingControlLock(bool value)
    {
        IsFeedingControlLocked = value;
        if (value) movingSteering = false;
    }
    private bool ManualViewBlocked => IsFeedingControlLocked || DiverStartupGate.IsBlocked(gameObject.scene);
    [Tooltip("View camera offset in Face bone local coordinates. Positive Z moves the camera toward the face's forward direction.")]
    [SerializeField] private Vector3 viewCameraPositionOffset = new Vector3(0f, 0f, 0.1f);

    [Header("View limits")]
    [SerializeField, Min(1f)] private float verticalStep = 40f;
    [SerializeField, Min(1f)] private float horizontalStep = 45f;
    [SerializeField, Range(0f, 89f)] private float maxVerticalAngle = 80f;
    [SerializeField, Range(1f, 180f)] private float maxHorizontalAngle = 90f;
    [SerializeField, Min(0.01f)] private float viewSmoothTime = 0.22f;
    [SerializeField, Min(0.01f)] private float bodyTurnDuration = 0.8f;

    private float pitch;
    private float yaw;
    private float targetPitch;
    private float targetYaw;
    private float pitchSmoothVelocity;
    private float yawSmoothVelocity;
    private Quaternion bodyTurnStart;
    private Quaternion bodyTurnEnd;
    private float bodyTurnElapsed;
    private bool bodyTurning;
    private bool faceFollowsBodyRoot;
    private bool faceFollowsSpine005;
    private Quaternion bodyRootRestRotation;
    private Vector3 bodyRootRestPosition;
    private Quaternion faceBoneRestRotation;
    private Vector3 faceBoneRestPosition;
    private Quaternion faceRotationRelativeToBody;
    private Vector3 facePositionRelativeToBody;
    private float movementCounterLean;
    private Quaternion previousSpineOffset = Quaternion.identity;
    private Quaternion previousSpineOutput;
    private bool spineOffsetApplied;
    private Vector3 bodyForwardInRoot = Vector3.forward;
    private readonly DiverFacingTransition facingTransition = new DiverFacingTransition();
    private bool aligningToView;
    private int alignmentVersion;
    private bool movingSteering;
    public void SetMovingSteering(bool value) => movingSteering = value;

    private Transform ResolveBodyRoot()
    {
        // Imported animation bones must not own persistent world movement/heading.
        if (!bodyRoot || (bodyRoot != transform && bodyRoot.IsChildOf(transform))) bodyRoot = transform;
        return bodyRoot;
    }

    private void Awake()
    {
        if(gameObject.scene.name=="Main" && !GetComponent<DiverAnimationController>())
            gameObject.AddComponent<DiverAnimationController>();
        ResolveBodyRoot();
        faceFollowsSpine005 = faceBone != null && spine005Bone != null &&
                              (faceBone == spine005Bone || faceBone.IsChildOf(spine005Bone));
        if (bodyRoot == null || faceBone == null)
            return;

        faceFollowsBodyRoot = faceBone == bodyRoot || faceBone.IsChildOf(bodyRoot);
        bodyRootRestRotation = bodyRoot.rotation;
        bodyRootRestPosition = bodyRoot.position;
        faceBoneRestRotation = faceBone.rotation;
        faceBoneRestPosition = faceBone.position;
        faceRotationRelativeToBody = Quaternion.Inverse(bodyRootRestRotation) * faceBoneRestRotation;
        facePositionRelativeToBody = Quaternion.Inverse(bodyRootRestRotation) * (faceBoneRestPosition - bodyRootRestPosition);
        Vector3 forward = Vector3.ProjectOnPlane(faceBone.forward, Vector3.up);
        if (forward.sqrMagnitude > .0001f) bodyForwardInRoot = Quaternion.Inverse(bodyRoot.rotation) * forward.normalized;
    }

    /// <summary>Allows the movement controller to supply its opposite spine lean.</summary>
    public void SetMovementCounterLean(float degrees)
    {
        movementCounterLean = degrees;
    }

    /// <summary>Supplies the shared spine reference when it is set on the movement component.</summary>
    public void SetSpine005Bone(Transform bone)
    {
        if (spine005Bone != null || bone == null)
            return;
        spine005Bone = bone;
        faceFollowsSpine005 = faceBone != null && (faceBone == bone || faceBone.IsChildOf(bone));
        spineOffsetApplied = false;
    }

    /// <summary>Turns the body toward the current horizontal view direction and recenters pitch/yaw.</summary>
    public void RecenterVerticalView()
    {
        if (ManualViewBlocked) return;
        InterruptActionAlignment();
        targetPitch = 0f;
        pitchSmoothVelocity = 0f;
    }

    /// <summary>Turns the body toward the current horizontal view direction and recenters pitch/yaw.</summary>
    public void AlignBodyToView()
    {
        if (ManualViewBlocked) return;
        TryAlignBodyToView(out _);
    }

    public bool TryAlignBodyToView(out int version)
    {
        version = alignmentVersion;
        if (!isActiveAndEnabled || DiverStartupGate.IsBlocked(gameObject.scene) || !ResolveBodyRoot() || !faceBone || !viewCamera)
        {
            Debug.LogWarning("[DiverView] Cannot align: check active view controller, Face bone and view camera.", this);
            return false;
        }
        Vector3 requestedForward = viewCamera != null
            ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up)
            : Vector3.zero;
        if (requestedForward.sqrMagnitude < 0.0001f && bodyRoot != null)
            requestedForward = Vector3.ProjectOnPlane(bodyRoot.forward, Vector3.up);

        targetPitch = 0f;
        targetYaw = 0f;
        if (bodyRoot == null || requestedForward.sqrMagnitude < 0.0001f)
            return false;

        Vector3 bodyForward = BodyForward;
        if (bodyForward.sqrMagnitude < 0.0001f)
            bodyForward = Vector3.forward;
        float turnAngle = Vector3.SignedAngle(bodyForward, requestedForward.normalized, Vector3.up);
        bodyTurnStart = bodyRoot.rotation;
        bodyTurnEnd = Quaternion.AngleAxis(turnAngle, Vector3.up) * bodyTurnStart;
        bodyTurnElapsed = 0f;
        bodyTurning = false;
        aligningToView = true;
        facingTransition.Begin(turnAngle);
        pitchSmoothVelocity = yawSmoothVelocity = 0f;
        version = ++alignmentVersion;
        return true;
    }

    private void LateUpdate()
    {
        // Keep the optical camera attached during robot boot; command entry points remain gated.
        pitch = Mathf.SmoothDampAngle(pitch, targetPitch, ref pitchSmoothVelocity, viewSmoothTime);
        if (aligningToView && bodyRoot)
        {
            facingTransition.Step(Time.deltaTime, bodyTurnDuration, pitch);
            bodyRoot.rotation = Quaternion.AngleAxis(facingTransition.BodyYaw, Vector3.up) * bodyTurnStart;
            yaw = facingTransition.RemainingYaw;
            if (facingTransition.Complete)
            {
                aligningToView = false;
                pitch = targetPitch = yaw = targetYaw = 0f;
                pitchSmoothVelocity = yawSmoothVelocity = 0f;
            }
        }
        else yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawSmoothVelocity, viewSmoothTime);

        if (!aligningToView && bodyTurning && bodyRoot)
        {
            bodyTurnElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(bodyTurnElapsed / bodyTurnDuration);
            bodyRoot.rotation = Quaternion.Slerp(bodyTurnStart, bodyTurnEnd, Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f)
                bodyTurning = false;
        }

        ApplySpineViewOffset();

        if (faceBone && viewCamera)
        {
            Transform cameraTransform = viewCamera.transform;
            Vector3 facePosition = faceBone.position;
            Quaternion faceRotation = faceBone.rotation;
            if (bodyRoot != null && !faceFollowsBodyRoot)
            {
                Quaternion bodyDelta = bodyRoot.rotation * Quaternion.Inverse(bodyRootRestRotation);
                Quaternion faceAnimationDelta = Quaternion.Inverse(faceBoneRestRotation) * faceBone.rotation;
                facePosition = bodyRoot.position + bodyRoot.rotation * facePositionRelativeToBody +
                               bodyDelta * (faceBone.position - faceBoneRestPosition);
                faceRotation = bodyRoot.rotation * faceRotationRelativeToBody * faceAnimationDelta;
            }

            facePosition += faceRotation * viewCameraPositionOffset;
            Quaternion cameraViewOffset = faceFollowsSpine005
                ? Quaternion.identity
                : Quaternion.Euler(pitch, yaw, 0f);
            cameraTransform.SetPositionAndRotation(facePosition, faceRotation * cameraViewOffset);
        }

        if (spine006Bone && billboardCamera)
            billboardCamera.transform.position = spine006Bone.position;

    }

    private void ApplySpineViewOffset()
    {
        if (spine005Bone == null)
            return;

        Quaternion baseRotation = spine005Bone.localRotation;
        if (spineOffsetApplied && Quaternion.Angle(baseRotation, previousSpineOutput) < 0.05f)
            baseRotation = baseRotation * Quaternion.Inverse(previousSpineOffset);

        Quaternion newOffset = Quaternion.Euler(pitch + movementCounterLean, yaw, 0f);
        spine005Bone.localRotation = baseRotation * newOffset;
        previousSpineOffset = newOffset;
        previousSpineOutput = spine005Bone.localRotation;
        spineOffsetApplied = true;
    }

    // Positive local X rotates Unity's +Z forward vector downward.
    public void LookUp() { if (!ManualViewBlocked) { InterruptActionAlignment(); targetPitch = Mathf.Clamp(targetPitch - verticalStep, -maxVerticalAngle, maxVerticalAngle); } }

    public void LookDown() { if (!ManualViewBlocked) { InterruptActionAlignment(); targetPitch = Mathf.Clamp(targetPitch + verticalStep, -maxVerticalAngle, maxVerticalAngle); } }

    public void LookRight() => LookHorizontal(horizontalStep);

    public void LookLeft() => LookHorizontal(-horizontalStep);

    private void LookHorizontal(float angle)
    {
        if (ManualViewBlocked) return;
        InterruptActionAlignment();
        if (!movingSteering)
        { targetYaw = Mathf.Clamp(targetYaw + angle, -maxHorizontalAngle, maxHorizontalAngle); return; }
        // Use the same smooth whole-body turn as TurnAround. Forward movement
        // already reads BodyForward every frame, so the course follows the turn.
        targetYaw = 0f;
        StartBodyTurn(angle);
    }

    public void TurnAround()
    {
        if (ManualViewBlocked) return;
        InterruptActionAlignment();
        targetPitch = 0f;
        targetYaw = 0f;
        StartBodyTurn(180f);
    }

    private void StartBodyTurn(float angle)
    {
        if (bodyRoot == null)
            return;
        bodyTurnStart = bodyRoot.rotation;
        bodyTurnEnd = Quaternion.AngleAxis(angle, Vector3.up) * bodyTurnStart;
        bodyTurnElapsed = 0f;
        bodyTurning = true;
    }

    public void ResetView()
    {
        if (ManualViewBlocked) return;
        InterruptActionAlignment();
        targetPitch = 0f;
        targetYaw = 0f;
    }

    internal void PrepareNextExperience(Vector3 position, Quaternion rotation)
    {
        InterruptActionAlignment();
        bodyTurning = aligningToView = movingSteering = false;
        IsFeedingControlLocked = false;
        pitch = yaw = targetPitch = targetYaw = 0f;
        pitchSmoothVelocity = yawSmoothVelocity = 0f;
        movementCounterLean = 0f;
        ResolveBodyRoot().SetPositionAndRotation(position, rotation);
    }

    private void InterruptActionAlignment()
    {
        alignmentVersion++;
        if (!aligningToView) return;
        aligningToView = false;
        targetPitch = pitch;
        targetYaw = yaw;
        pitchSmoothVelocity = yawSmoothVelocity = 0f;
    }
    private void OnDisable()
    {
        IsFeedingControlLocked = false;
        InterruptActionAlignment();
        bodyTurning = false;
    }
}
