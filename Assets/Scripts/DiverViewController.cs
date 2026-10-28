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
    [SerializeField] private Transform spine006Bone;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Camera billboardCamera;

    [Header("View limits")]
    [SerializeField, Min(1f)] private float verticalStep = 40f;
    [SerializeField, Min(1f)] private float horizontalStep = 45f;
    [SerializeField, Range(0f, 89f)] private float maxVerticalAngle = 80f;
    [SerializeField, Range(1f, 180f)] private float maxHorizontalAngle = 90f;

    private float pitch;
    private float yaw;

    private void LateUpdate()
    {
        if (faceBone && viewCamera)
        {
            Transform cameraTransform = viewCamera.transform;
            cameraTransform.SetPositionAndRotation(
                faceBone.position,
                faceBone.rotation * Quaternion.Euler(pitch, yaw, 0f));
        }

        if (spine006Bone && billboardCamera)
            billboardCamera.transform.position = spine006Bone.position;
    }

    public void LookUp() => pitch = Mathf.Clamp(pitch + verticalStep, -maxVerticalAngle, maxVerticalAngle);

    public void LookDown() => pitch = Mathf.Clamp(pitch - verticalStep, -maxVerticalAngle, maxVerticalAngle);

    public void LookRight() => yaw = Mathf.Clamp(yaw + horizontalStep, -maxHorizontalAngle, maxHorizontalAngle);

    public void LookLeft() => yaw = Mathf.Clamp(yaw - horizontalStep, -maxHorizontalAngle, maxHorizontalAngle);

    public void TurnAround()
    {
        // Reset the offset view first, then turn the body by a fixed half-turn.
        pitch = 0f;
        yaw = 0f;
        if (bodyRoot)
            bodyRoot.Rotate(Vector3.up, 180f, Space.World);
    }

    public void ResetView()
    {
        pitch = 0f;
        yaw = 0f;
    }
}
