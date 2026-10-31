using System;
using UnityEngine;

/// <summary>Provides a camera-relative right arm pose from one Kinect body.</summary>
public sealed class KinectScoopPose : MonoBehaviour
{
    [SerializeField] private AzureKinectSkeletonView skeleton;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector3 chestOffset = new Vector3(0f, -0.38f, 0.25f);
    [SerializeField, Min(0.01f)] private float handExtension = 0.09f;
    [SerializeField, Min(0.01f)] private float smoothingSeconds = 0.10f;
    [SerializeField, Range(0.1f, 1f)] private float maximumFrameAgeSeconds = 0.35f;

    public bool IsTracked { get; private set; }
    public bool IsEstimated { get; private set; }
    public Vector3 HandPosition { get; private set; }
    public Vector3 WristPosition { get; private set; }
    public Vector3 ElbowPosition { get; private set; }
    public Vector3 ShoulderPosition { get; private set; }
    public Vector3 ForearmDirection { get; private set; }
    public Quaternion HandRotation { get; private set; } = Quaternion.identity;
    public bool HasHandOrientation { get; private set; }
    public string TrackingMessage { get; private set; } = "Waiting for body";
    public uint BodyId { get; private set; }

    private readonly KinectScoopSolver solver = new KinectScoopSolver();
    private bool hasBody;
    private long lastFrame = -1;
    private float lastSampleTime, lastSeenBodyTime;
    private KinectScoopSolver.Output pose;

    private void Awake()
    {
        if (skeleton == null) skeleton = GetComponent<AzureKinectSkeletonView>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    public void SetTargetCamera(Camera camera) { if (camera) targetCamera = camera; }

    private void Update()
    {
        // Hold the selected ID through short dropouts. Reacquire only after a
        // prolonged absence, and only when there is exactly one candidate.
        if (hasBody && Time.unscaledTime - lastSeenBodyTime >= 2f)
            ResetTracking();
        if (targetCamera == null || skeleton == null)
        {
            LosePose("Camera or Kinect skeleton unavailable");
            return;
        }
        var snapshot = skeleton.LatestSnapshot;
        if (snapshot == null || (System.Diagnostics.Stopwatch.GetTimestamp() - snapshot.ReceivedTicks) /
            (double)System.Diagnostics.Stopwatch.Frequency > maximumFrameAgeSeconds)
        {
            LosePose("Waiting for a fresh body frame");
            return;
        }
        if (snapshot.FrameNumber != lastFrame)
        {
            lastFrame = snapshot.FrameNumber;
            AzureKinectCapture.Body body = null;
            if (!hasBody && snapshot.Bodies != null && snapshot.Bodies.Length == 1 && snapshot.Bodies[0] != null)
            {
                BodyId = snapshot.Bodies[0].Id;
                hasBody = true;
            }
            if (hasBody && snapshot.Bodies != null)
                foreach (var candidate in snapshot.Bodies)
                    if (candidate != null && candidate.Id == BodyId) { body = candidate; break; }
            if (body != null) lastSeenBodyTime = Time.unscaledTime;
            if (body == null || body.Joints == null || body.Joints.Length <= 15)
            {
                LosePose(hasBody ? "Selected body unavailable" : "Waiting for one body");
                return;
            }
            solver.HandExtension = Mathf.Max(0.01f, handExtension);
            solver.SmoothingSeconds = Mathf.Max(0.01f, smoothingSeconds);
            float now = Time.unscaledTime;
            float dt = lastSampleTime > 0f ? now - lastSampleTime : 0f;
            lastSampleTime = now;
            var input = new KinectScoopSolver.Input
            {
                Chest = Sample(body.Joints[2]), Shoulder = Sample(body.Joints[12]),
                Elbow = Sample(body.Joints[13]), Wrist = Sample(body.Joints[14]),
                Hand = Sample(body.Joints[15])
            };
            if (!solver.Step(input, dt, out pose))
            {
                LosePose("Chest, shoulder, elbow or wrist unavailable");
                return;
            }
            IsTracked = true;
            IsEstimated = pose.IsEstimated;
            TrackingMessage = pose.IsEstimated ? "Estimated from forearm" : "Hand measurement blended";
        }
        if (!IsTracked) return;
        Transform cameraTransform = targetCamera.transform;
        HandPosition = cameraTransform.TransformPoint(chestOffset + pose.Hand);
        WristPosition = cameraTransform.TransformPoint(chestOffset + pose.Wrist);
        ElbowPosition = cameraTransform.TransformPoint(chestOffset + pose.Elbow);
        ShoulderPosition = cameraTransform.TransformPoint(chestOffset + pose.Shoulder);
        ForearmDirection = cameraTransform.TransformDirection(pose.ForearmDirection).normalized;
        HasHandOrientation = pose.HasHandOrientation;
        if (HasHandOrientation)
            HandRotation = cameraTransform.rotation * pose.HandRotation;
    }

    private static KinectScoopSolver.JointSample Sample(AzureKinectNative.Joint joint) =>
        new KinectScoopSolver.JointSample(new Vector3(joint.X, joint.Y, joint.Z) * 0.001f,
            joint.Confidence, new Quaternion(joint.Qx, joint.Qy, joint.Qz, joint.Qw));

    private void LosePose(string message)
    {
        IsTracked = false;
        IsEstimated = false;
        TrackingMessage = message;
        lastSampleTime = 0f;
        solver.Reset();
    }

    /// <summary>Release the selected body so a new participant can be selected.</summary>
    public void SelectBody(uint id)
    {
        ResetTracking();
        BodyId = id;
        hasBody = true;
        lastSeenBodyTime = Time.unscaledTime;
    }

    /// <summary>Release the selected body so a new participant can be selected.</summary>
    public void ResetTracking()
    {
        hasBody = false;
        BodyId = 0;
        lastSeenBodyTime = 0f;
        lastFrame = -1;
        LosePose("Waiting for body");
    }

    private void OnDisable() => ResetTracking();
}

/// <summary>Pure pose calculation; all input coordinates are sensor-space metres.</summary>
public sealed class KinectScoopSolver
{
    public struct JointSample
    {
        public Vector3 Position;
        public int Confidence;
        public Quaternion Rotation;
        public bool HasRotation;
        public JointSample(Vector3 position, int confidence)
        {
            Position = position;
            Confidence = confidence;
            Rotation = Quaternion.identity;
            HasRotation = false;
        }
        public JointSample(Vector3 position, int confidence, Quaternion rotation)
        {
            Position = position;
            Confidence = confidence;
            float norm = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            HasRotation = float.IsFinite(norm) && norm > 0.0001f;
            Rotation = HasRotation ? new Quaternion(rotation.x / norm, rotation.y / norm, rotation.z / norm, rotation.w / norm) : Quaternion.identity;
        }
    }

    public struct Input { public JointSample Chest, Shoulder, Elbow, Wrist, Hand; }
    public struct Output
    {
        // Camera-local metres relative to the chest, with camera-facing axis conversion applied.
        public Vector3 Shoulder, Elbow, Wrist, Hand, ForearmDirection;
        public bool IsEstimated, HasHandOrientation;
        public Quaternion HandRotation;
    }

    public float HandExtension { get; set; } = 0.09f;
    public float SmoothingSeconds { get; set; } = 0.10f;
    private bool initialized, hasHandOffset;
    private Vector3 lastHandOffset;
    private float rejectedSeconds;
    private Output previous;

    public void Reset()
    {
        initialized = hasHandOffset = false;
        rejectedSeconds = 0f;
        previous = default;
    }

    private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
    private static bool Usable(JointSample p) => p.Confidence >= 2 && Finite(p.Position) && p.Position.z > 0f;
    private static Vector3 ToCamera(Vector3 sensorVector) => -sensorVector;

    public bool Step(Input input, float deltaSeconds, out Output output)
    {
        output = default;
        if (!Usable(input.Chest) || !Usable(input.Shoulder) || !Usable(input.Elbow) || !Usable(input.Wrist))
        { Reset(); return false; }
        Vector3 forearm = input.Wrist.Position - input.Elbow.Position;
        float length = forearm.magnitude;
        if (!float.IsFinite(length) || length < 0.04f || length > 0.75f)
        { Reset(); return false; }
        Vector3 direction = forearm / length;
        float extension = float.IsFinite(HandExtension) ? Mathf.Clamp(HandExtension, 0.01f, 0.25f) : 0.09f;
        Vector3 predicted = input.Wrist.Position + direction * extension;
        Vector3 measuredOffset = input.Hand.Position - input.Wrist.Position;
        float measuredLength = measuredOffset.magnitude;
        bool measured = Usable(input.Hand) && float.IsFinite(measuredLength) && measuredLength >= 0.02f &&
            measuredLength <= 0.22f && Vector3.Dot(measuredOffset / measuredLength, direction) >= 0.15f &&
            Vector3.Distance(input.Hand.Position, predicted) <= 0.15f;
        float dt = float.IsFinite(deltaSeconds) ? Mathf.Clamp(deltaSeconds, 0f, 0.5f) : 0f;
        if (measured && hasHandOffset && Vector3.Distance(measuredOffset, lastHandOffset) > 0.12f)
        {
            measured = false;
            rejectedSeconds += dt;
            // Allow a new stable measurement to establish itself after a short rejection period.
            if (rejectedSeconds >= 0.35f) hasHandOffset = false;
        }
        else if (measured) rejectedSeconds = 0f;
        if (measured) { lastHandOffset = measuredOffset; hasHandOffset = true; }
        Vector3 hand = measured ? Vector3.Lerp(predicted, input.Hand.Position, 0.65f) : predicted;
        Vector3 chest = input.Chest.Position;
        output = new Output { Shoulder = ToCamera(input.Shoulder.Position - chest), Elbow = ToCamera(input.Elbow.Position - chest),
            Wrist = ToCamera(input.Wrist.Position - chest), Hand = ToCamera(hand - chest),
            ForearmDirection = ToCamera(direction), IsEstimated = !measured,
            HandRotation = input.Hand.Rotation, HasHandOrientation = input.Hand.HasRotation };
        if (initialized && dt > 0f)
        {
            float seconds = float.IsFinite(SmoothingSeconds) ? Mathf.Max(0.01f, SmoothingSeconds) : 0.10f;
            float alpha = 1f - Mathf.Exp(-dt / seconds);
            output.Shoulder = Vector3.Lerp(previous.Shoulder, output.Shoulder, alpha);
            output.Elbow = Vector3.Lerp(previous.Elbow, output.Elbow, alpha);
            output.Wrist = Vector3.Lerp(previous.Wrist, output.Wrist, alpha);
            output.Hand = Vector3.Lerp(previous.Hand, output.Hand, alpha);
            if (output.HasHandOrientation && previous.HasHandOrientation)
                output.HandRotation = Quaternion.Slerp(previous.HandRotation, output.HandRotation, alpha);
            Vector3 smoothedDirection = output.Wrist - output.Elbow;
            if (smoothedDirection.sqrMagnitude > 1e-8f) output.ForearmDirection = smoothedDirection.normalized;
        }
        initialized = true;
        previous = output;
        return true;
    }
}

