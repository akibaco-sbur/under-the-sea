using UnityEngine;

/// <summary>Drives a character's upper-arm, forearm and hand bones from a tracked arm pose.</summary>
public sealed class KinectArmRigFollower : MonoBehaviour
{
    [SerializeField] private Transform targetCamera;
    [SerializeField] private Transform upperArm;
    [SerializeField] private Transform forearm;
    [SerializeField] private Transform hand;
    [SerializeField, Min(0.01f)] private float positionSmoothing = 0.08f;
    [Header("Model bone-axis corrections (local Euler angles)")]
    [SerializeField] private Vector3 upperArmRotationCorrection = new Vector3(0f, -40f, 0f);
    [SerializeField] private Vector3 forearmRotationCorrection = new Vector3(-5f, -90f, 15f);
    [SerializeField] private Vector3 handOrientationCorrection = new Vector3(0f, 105f, 90f);
    [Header("Palm position adjustment (camera-relative metres)")]
    [Tooltip("Shift the whole arm relative to the contact target. X: camera right, Y: camera up, Z: camera forward. Can be tuned during Play.")]
    [SerializeField] private Vector3 palmPositionOffset = new Vector3(0.07f, -0.1f, -0.09f);
    [Header("Arm size during feeding")]
    [Tooltip("Uniform size multiplier for the upper arm, forearm and hand. 1 keeps the original size. Palm alignment is applied after scaling.")]
    [SerializeField, Min(0.01f)] private float armScaleMultiplier = 0.3f;

    private Quaternion upperArmRestRotation;
    private Quaternion forearmRestRotation;
    private Vector3 upperArmRestAxis;
    private Vector3 forearmRestAxis;
    private bool initialized;
    private bool calibrated;
    private Vector3 cameraLocalShoulderOffset;
    private Vector3 smoothedShoulder, smoothedElbow, smoothedWrist;
    private bool alignInitialShoulder = true;
    private Vector3[] savedLocalPositions;
    private Quaternion[] savedLocalRotations;
    private Vector3 savedUpperArmScale;
    private Vector3 palmCenterLocal;

    public Vector3 PalmCenterPosition => hand != null ? hand.TransformPoint(palmCenterLocal) : Vector3.zero;

    public bool IsReady => upperArm != null && forearm != null && hand != null;

    private void Awake()
    {
        if (targetCamera == null && Camera.main != null) targetCamera = Camera.main.transform;
        if (upperArm == null) upperArm = FindChild(transform, "upper_arm.R");
        if (forearm == null) forearm = FindChild(transform, "forearm.R");
        if (hand == null) hand = FindChild(transform, "hand.R");
        CacheRestPose();
        CachePalmCenter();
    }

    /// <summary>Apply world-space shoulder, elbow and wrist positions from the tracked pose.</summary>
    public void SetTargetCamera(Transform cameraTransform) => targetCamera = cameraTransform;

    /// <summary>Main uses the same absolute joints as its food contact target.</summary>
    public void SetInitialShoulderAlignment(bool align)
    {
        alignInitialShoulder = align;
        ResetCalibration();
    }

    public void SaveLocalPose()
    {
        if (!IsReady || savedLocalPositions != null) return;
        Transform[] bones = { upperArm, forearm, hand };
        savedLocalPositions = new Vector3[bones.Length];
        savedLocalRotations = new Quaternion[bones.Length];
        savedUpperArmScale = upperArm.localScale;
        for (int i = 0; i < bones.Length; i++)
        {
            savedLocalPositions[i] = bones[i].localPosition;
            savedLocalRotations[i] = bones[i].localRotation;
        }
    }

    public void RestoreLocalPose()
    {
        if (IsReady && savedLocalPositions != null)
        {
            upperArm.localScale = savedUpperArmScale;
            Transform[] bones = { upperArm, forearm, hand };
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = savedLocalPositions[i];
                bones[i].localRotation = savedLocalRotations[i];
            }
        }
        savedLocalPositions = null;
        savedLocalRotations = null;
        ResetCalibration();
    }

    /// <summary>Apply absolute world-space shoulder, elbow, wrist and hand orientation.</summary>
    public bool ApplyPose(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion handRotation,
        bool hasHandOrientation, bool tracked, float deltaTime)
    {
        if (!IsReady || targetCamera == null || !tracked)
            return false;

        // Always multiply the saved scale, so live adjustments do not accumulate.
        // The forearm and hand inherit the uniform scale from their parent bone.
        if (savedLocalPositions != null)
            upperArm.localScale = savedUpperArmScale * (float.IsFinite(armScaleMultiplier)
                ? Mathf.Max(0.01f, armScaleMultiplier) : 1f);

        if (!calibrated)
        {
            // Preserve the model's current shoulder placement while mapping all
            // measured joints into that same camera-relative coordinate space.
            cameraLocalShoulderOffset = alignInitialShoulder
                ? targetCamera.InverseTransformPoint(upperArm.position) - targetCamera.InverseTransformPoint(shoulder)
                : Vector3.zero;
            calibrated = true;
        }

        Vector3 offset = targetCamera.TransformVector(cameraLocalShoulderOffset);
        Vector3 targetShoulder = shoulder + offset;
        Vector3 targetElbow = elbow + offset;
        Vector3 targetWrist = wrist + offset;
        if (!initialized)
        {
            smoothedShoulder = targetShoulder;
            smoothedElbow = targetElbow;
            smoothedWrist = targetWrist;
            initialized = true;
        }
        else
        {
            float alpha = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, positionSmoothing));
            smoothedShoulder = Vector3.Lerp(smoothedShoulder, targetShoulder, alpha);
            smoothedElbow = Vector3.Lerp(smoothedElbow, targetElbow, alpha);
            smoothedWrist = Vector3.Lerp(smoothedWrist, targetWrist, alpha);
        }

        Vector3 upperDirection = smoothedElbow - smoothedShoulder;
        Vector3 forearmDirection = smoothedWrist - smoothedElbow;
        if (upperDirection.sqrMagnitude < 1e-6f || forearmDirection.sqrMagnitude < 1e-6f)
            return false;

        upperArm.position = smoothedShoulder;
        upperArm.rotation = Quaternion.FromToRotation(upperArmRestRotation * upperArmRestAxis,
            upperDirection.normalized) * upperArmRestRotation * Quaternion.Euler(upperArmRotationCorrection);
        forearm.position = smoothedElbow;
        forearm.rotation = Quaternion.FromToRotation(forearmRestRotation * forearmRestAxis,
            forearmDirection.normalized) * forearmRestRotation * Quaternion.Euler(forearmRotationCorrection);
        hand.position = smoothedWrist;
        if (hasHandOrientation)
            hand.rotation = handRotation * Quaternion.Euler(handOrientationCorrection);
        return true;
    }

    /// <summary>Place the anatomical palm center at the same point used for food contact.</summary>
    public bool ApplyPoseAtPalm(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion handRotation,
        bool hasHandOrientation, bool tracked, float deltaTime, Vector3 palmTarget)
    {
        if (!ApplyPose(shoulder, elbow, wrist, handRotation, hasHandOrientation, tracked, deltaTime))
            return false;
        Vector3 adjustedPalmTarget = palmTarget + targetCamera.rotation * palmPositionOffset;
        Vector3 translation = adjustedPalmTarget - PalmCenterPosition;
        // Translate the entire floating arm, preserving the elbow and wrist pose.
        upperArm.position += translation;
        if (!forearm.IsChildOf(upperArm)) forearm.position += translation;
        if (!hand.IsChildOf(upperArm) && !hand.IsChildOf(forearm)) hand.position += translation;
        return true;
    }

    private void CachePalmCenter()
    {
        if (!IsReady) return;
        string[] fingerNames = { "f_index.01.R", "f_middle.01.R", "f_ring.01.R", "f_pinky.01.R" };
        Vector3 knuckleSum = Vector3.zero;
        int count = 0;
        foreach (string fingerName in fingerNames)
        {
            Transform finger = FindChild(hand, fingerName);
            if (finger == null) continue;
            knuckleSum += hand.InverseTransformPoint(finger.position);
            count++;
        }
        // Palm center lies between the wrist pivot and the non-thumb knuckles.
        if (count > 0)
            palmCenterLocal = knuckleSum / count * 0.5f;
        else
        {
            Vector3 forearmVector = hand.position - forearm.position;
            palmCenterLocal = hand.InverseTransformVector(forearmVector * 0.3f);
            Debug.LogWarning("[KinectArmRig] Finger base bones were not found; palm center is estimated from forearm length.", this);
        }
    }

    private void CacheRestPose()
    {
        if (upperArm == null || forearm == null || hand == null)
            return;

        upperArmRestRotation = upperArm.rotation;
        forearmRestRotation = forearm.rotation;
        Vector3 upperAxis = forearm.position - upperArm.position;
        Vector3 forearmAxis = hand.position - forearm.position;
        upperArmRestAxis = upperAxis.sqrMagnitude > 1e-6f
            ? Quaternion.Inverse(upperArmRestRotation) * upperAxis.normalized : Vector3.forward;
        forearmRestAxis = forearmAxis.sqrMagnitude > 1e-6f
            ? Quaternion.Inverse(forearmRestRotation) * forearmAxis.normalized : Vector3.forward;
    }

    public void ResetCalibration()
    {
        calibrated = false;
        initialized = false;
    }

    private static Transform FindChild(Transform root, string targetName)
    {
        foreach (Transform child in root)
        {
            if (child.name == targetName)
                return child;
            Transform match = FindChild(child, targetName);
            if (match != null)
                return match;
        }
        return null;
    }
}
