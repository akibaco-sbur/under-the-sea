using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal sealed class AzureKinectCapture
{
    internal sealed class Body
    {
        public uint Id;
        public AzureKinectNative.Joint[] Joints;
        public float RightHandArea;
        public AzureKinectHandDepth.Region RightHandRegion;
    }
    internal sealed class Snapshot
    {
        public Body[] Bodies;
        public ulong TimestampMicroseconds;
        public long ReceivedTicks;
        public long FrameNumber;
        public AzureKinectHandDepth.Image DepthImage;
    }

    private readonly object gate = new object();
    private readonly string directory;
    private readonly int processingMode;
    private readonly bool estimateHandShape;
    private Thread thread;
    private volatile bool stopping;
    private IntPtr tracker;
    private bool shutdown;
    private Snapshot latest;
    private string status = "Stopped";
    private long captures, results, dropped;

    internal AzureKinectCapture(string directory, int processingMode, bool estimateHandShape = false)
    { this.directory = directory; this.processingMode = processingMode; this.estimateHandShape = estimateHandShape; }
    internal bool IsAlive => thread != null && thread.IsAlive;
    internal string Status { get { lock (gate) return status; } }
    internal Snapshot Latest { get { lock (gate) return latest; } }
    internal long Captures => Interlocked.Read(ref captures);
    internal long Results => Interlocked.Read(ref results);
    internal long Dropped => Interlocked.Read(ref dropped);
    private void SetStatus(string value) { lock (gate) status = value; }

    internal void Start()
    {
        if (IsAlive) return;
        stopping = false;
        thread = new Thread(Run) { IsBackground = true, Name = "Azure Kinect Capture" };
        thread.Start();
    }

    internal bool Stop(int waitMilliseconds = 2000)
    {
        stopping = true;
        lock (gate)
        {
            latest = null;
            if (tracker != IntPtr.Zero && !shutdown)
            { AzureKinectNative.k4abt_tracker_shutdown(tracker); shutdown = true; }
        }
        bool finished = thread == null || thread.Join(waitMilliseconds);
        if (!finished) SetStatus("Stopping — waiting for native initialization to finish.");
        return finished;
    }

    private void Run()
    {
        IntPtr device = IntPtr.Zero;
        try
        {
            SetStatus("Loading local SDK");
            AzureKinectNative.Load(directory);
            if (AzureKinectNative.k4a_device_get_installed_count() == 0)
                throw new InvalidOperationException("No Azure Kinect detected. Check USB 3 and external power.");
            if (AzureKinectNative.k4a_device_open(0, out device) != 0)
                throw new InvalidOperationException("Cannot open Kinect. Close other viewers using the device.");
            var config = new AzureKinectNative.DeviceConfiguration { DepthMode = 2, CameraFps = 1 };
            // NFOV unbinned / 15 FPS / color OFF / standalone / LED enabled.
            if (AzureKinectNative.k4a_device_start_cameras(device, ref config) != 0)
                throw new InvalidOperationException("Cannot start depth camera. Check USB bandwidth and power.");
            if (AzureKinectNative.k4a_device_get_calibration(device, config.DepthMode, 0, out var calibration) != 0)
                throw new InvalidOperationException("Depth calibration unavailable.");
            SetStatus("Creating body tracker (first start can take several seconds)");
            IntPtr model = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(Path.Combine(directory, "dnn_model_2_0_lite_op11.onnx"));
            IntPtr created;
            try
            {
                var trackerConfig = new AzureKinectNative.TrackerConfiguration { ProcessingMode = processingMode, ModelPath = model };
                if (AzureKinectNative.k4abt_tracker_create(ref calibration, trackerConfig, out created) != 0)
                    throw new InvalidOperationException("Body tracker creation failed. Check model/runtime or try CPU mode.");
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(model); }
            lock (gate) { tracker = created; shutdown = false; }
            int pending = 0;
            var handDepth = estimateHandShape ? new AzureKinectHandDepth() : null;
            while (!stopping)
            {
                // Drain completed frames first. At most two inputs are ever in flight.
                for (int i = 0; i < 2 && !stopping; i++)
                {
                    int pop = AzureKinectNative.k4abt_tracker_pop_result(created, out var frame, 0);
                    if (pop == 2) break;
                    if (pop != 0) { if (stopping) break; throw new InvalidOperationException("Body tracker stopped producing results."); }
                    try
                    {
                        pending = Math.Max(0, pending - 1);
                        uint count = AzureKinectNative.k4abt_frame_get_num_bodies(frame);
                        var bodies = new Body[Math.Min(count, 6u)];
                        for (uint b = 0; b < bodies.Length; b++)
                        {
                            if (AzureKinectNative.k4abt_frame_get_body_skeleton(frame, b, out var skeleton) != 0)
                                throw new InvalidOperationException("Cannot read body skeleton.");
                            bodies[b] = new Body { Id = AzureKinectNative.k4abt_frame_get_body_id(frame, b), Joints = skeleton.Joints };
                        }
                        var depthImage = handDepth?.Measure(frame, ref calibration, bodies);
                        var snapshot = new Snapshot { DepthImage = depthImage, Bodies = bodies, TimestampMicroseconds = AzureKinectNative.k4abt_frame_get_device_timestamp_usec(frame), ReceivedTicks = Stopwatch.GetTimestamp(), FrameNumber = Interlocked.Increment(ref results) };
                        lock (gate) latest = snapshot;
                        SetStatus(count == 0 ? "Depth live — no person detected. Stand 1–4 m in front of the sensor." : "Live skeleton tracking");
                    }
                    finally { AzureKinectNative.k4abt_frame_release(frame); }
                }
                int captureResult = AzureKinectNative.k4a_device_get_capture(device, out var capture, 50);
                if (captureResult == 2) continue;
                if (captureResult != 0) { if (stopping) break; throw new InvalidOperationException("Depth stream disconnected. Check USB/power."); }
                try
                {
                    Interlocked.Increment(ref captures);
                    if (pending >= 2) { Interlocked.Increment(ref dropped); continue; }
                    int queued = AzureKinectNative.k4abt_tracker_enqueue_capture(created, capture, 0);
                    if (queued == 0) pending++;
                    else if (queued == 2) Interlocked.Increment(ref dropped);
                    else if (!stopping) throw new InvalidOperationException("Body tracker rejected depth capture.");
                }
                finally { AzureKinectNative.k4a_capture_release(capture); }
            }
            SetStatus("Stopped");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
        finally
        {
            lock (gate)
            {
                latest = null;
                if (tracker != IntPtr.Zero)
                {
                    if (!shutdown) AzureKinectNative.k4abt_tracker_shutdown(tracker);
                    AzureKinectNative.k4abt_tracker_destroy(tracker);
                    tracker = IntPtr.Zero;
                }
            }
            if (device != IntPtr.Zero)
            { AzureKinectNative.k4a_device_stop_cameras(device); AzureKinectNative.k4a_device_close(device); }
        }
    }
}
