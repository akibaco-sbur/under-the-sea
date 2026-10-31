using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public sealed class AzureKinectSkeletonView : MonoBehaviour
{
    public enum ProcessingBackend { Cpu = 1, DirectML = 4 }
    [SerializeField] private ProcessingBackend processingBackend = ProcessingBackend.DirectML;
    [SerializeField, Range(0.2f, 3f)] private float staleSeconds = 1f;
    [SerializeField, Range(1, 3)] private int minimumConfidence = 2;
    [SerializeField] private bool renderSkeleton = true;
    [SerializeField] private bool showStatusOverlay = true;
    [SerializeField] private bool estimateHandShape;
    private AzureKinectCapture capture;
    private long displayedFrame = -1;
    private float retryAt;
    private bool manualStop;
    private readonly Dictionary<uint, BodyVisual> visuals = new Dictionary<uint, BodyVisual>();
    private static readonly int[] Parents = { -1,0,1,2,2,4,5,6,7,8,7,2,11,12,13,14,15,14,0,18,19,20,0,22,23,24,3,26,26,28,26,30 };

    public long SensorFrames => capture?.Captures ?? 0;
    public long BodyFrames => capture?.Results ?? 0;
    public ulong DeviceTimestampMicroseconds => capture?.Latest?.TimestampMicroseconds ?? 0;
    public int BodyCount { get { var snapshot = capture?.Latest; return IsFresh(snapshot) ? snapshot.Bodies.Length : 0; } }
    public string TrackingStatus => capture?.Status ?? "Stopped";
    internal string RgbStatus => capture?.RgbStatus ?? "RGB recognition stopped";
    internal MediaPipeHandWorker.Result LatestRgb => capture?.LatestRgb;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        manualStop = false;
        StartCapture();
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
#endif
    }
    private void StartCapture()
    {
        if (capture != null && capture.IsAlive) return;
        capture = new AzureKinectCapture(Path.GetFullPath(Path.Combine(Application.dataPath, "../.Local/AzureKinect/Runtime")), (int)processingBackend, estimateHandShape);
        displayedFrame = -1;
        capture.Start();
    }
    public void StartTracking() { manualStop = false; StartCapture(); }
    public void StopTracking() { manualStop = true; capture?.Stop(); }
    private bool IsFresh(AzureKinectCapture.Snapshot snapshot) => snapshot != null && (Stopwatch.GetTimestamp() - snapshot.ReceivedTicks) / (double)Stopwatch.Frequency < staleSeconds;
    internal AzureKinectCapture.Snapshot LatestSnapshot { get { var snapshot = capture?.Latest; return IsFresh(snapshot) ? snapshot : null; } }

    /// <summary>Configure whether this tracker draws its diagnostic skeleton and status overlay.</summary>
    public void SetDebugVisualization(bool drawSkeleton, bool drawStatusOverlay)
    {
        renderSkeleton = drawSkeleton;
        showStatusOverlay = drawStatusOverlay;
    }

    private void Update()
    {
        var snapshot = capture?.Latest;
        if (!IsFresh(snapshot)) { foreach (var visual in visuals.Values) visual.Root.SetActive(false); }
        else if (renderSkeleton && snapshot.FrameNumber != displayedFrame)
        {
            displayedFrame = snapshot.FrameNumber;
            foreach (var visual in visuals.Values) visual.Root.SetActive(false);
            foreach (var body in snapshot.Bodies)
            {
                if (!visuals.TryGetValue(body.Id, out var visual))
                {
                    visual = new BodyVisual(body.Id, transform);
                    visuals.Add(body.Id, visual);
                }
                visual.Root.SetActive(true);
                visual.Apply(body.Joints, minimumConfidence);
            }
            // Remove lost IDs rather than accumulating visual objects forever.
            var lost = new List<uint>();
            foreach (var pair in visuals) if (!pair.Value.Root.activeSelf) lost.Add(pair.Key);
            foreach (uint id in lost) { visuals[id].Dispose(); visuals.Remove(id); }
        }
        if (!manualStop && capture != null && !capture.IsAlive && Time.unscaledTime >= retryAt)
        { retryAt = Time.unscaledTime + 5f; StartCapture(); }
    }

    private void OnGUI()
    {
        if (!showStatusOverlay) return;
        GUILayout.BeginArea(new Rect(16, 16, 760, 150), GUI.skin.box);
        GUILayout.Label("AZURE KINECT — DEPTH SKELETON / RGB OFF");
        GUILayout.Label(TrackingStatus);
        GUILayout.Label($"Sensor {SensorFrames} | Body frames {BodyFrames} | Bodies {BodyCount} | Timestamp {DeviceTimestampMicroseconds} us | Dropped {capture?.Dropped ?? 0}");
        GUILayout.Label("Sensor coordinates: metres, +X right, +Y up, +Z away. Medium/high-confidence joints only.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop")) StopTracking();
        if (GUILayout.Button("Start / Retry")) StartTracking();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    private void BeforeAssemblyReload() { FinishCapture(); }
#endif
    private void FinishCapture()
    {
        // tracker_create has no cancellation API. Never unload its managed caller while
        // native initialization is still running. Manual Stop remains responsive, but
        // leaving Play/reloading waits for initialization, then shutdown/join/destroy.
        if (capture != null && !capture.Stop()) capture.Stop(System.Threading.Timeout.Infinite);
    }
    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
#endif
        FinishCapture();
        foreach (var visual in visuals.Values) visual.Dispose();
        visuals.Clear();
    }

    private sealed class BodyVisual
    {
        internal readonly GameObject Root;
        private readonly Transform[] joints = new Transform[32];
        private readonly LineRenderer[] bones = new LineRenderer[32];
        private readonly Material material;

        internal BodyVisual(uint id, Transform parent)
        {
            Root = new GameObject($"TrackedBody{id}");
            Root.transform.SetParent(parent, false);
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.HSVToRGB((id * 0.6180339f) % 1f, 0.65f, 1f));
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                joint.name = $"Joint{i}";
                Object.Destroy(joint.GetComponent<Collider>());
                joint.transform.SetParent(Root.transform, false);
                joint.transform.localScale = Vector3.one * 0.035f;
                joint.GetComponent<Renderer>().sharedMaterial = material;
                joints[i] = joint.transform;
                if (Parents[i] < 0) continue;
                var bone = new GameObject($"Bone{i}").AddComponent<LineRenderer>();
                bone.transform.SetParent(Root.transform, false);
                bone.useWorldSpace = false;
                bone.positionCount = 2;
                bone.startWidth = bone.endWidth = 0.015f;
                bone.sharedMaterial = material;
                bones[i] = bone;
            }
        }
        internal void Apply(AzureKinectNative.Joint[] data, int confidence)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = data[i];
                var position = new Vector3(joint.X, -joint.Y, joint.Z) * 0.001f;
                joints[i].localPosition = position;
                bool valid = IsValid(joint, confidence);
                joints[i].gameObject.SetActive(valid);
                if (bones[i] == null) continue;
                int parent = Parents[i];
                bones[i].enabled = valid && IsValid(data[parent], confidence);
                if (bones[i].enabled)
                {
                    bones[i].SetPosition(0, position);
                    bones[i].SetPosition(1, new Vector3(data[parent].X, -data[parent].Y, data[parent].Z) * 0.001f);
                }
            }
        }
        private static bool IsValid(AzureKinectNative.Joint joint, int confidence) => joint.Confidence >= confidence && float.IsFinite(joint.X) && float.IsFinite(joint.Y) && float.IsFinite(joint.Z);
        internal void Dispose() { Object.Destroy(Root); Object.Destroy(material); }
    }
}
