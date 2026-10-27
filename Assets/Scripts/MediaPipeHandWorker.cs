using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

// One bounded in-memory queue keeps camera capture independent of model latency.
internal sealed class MediaPipeHandWorker : IDisposable
{
    internal sealed class Frame
    {
        internal int Width, Height;
        internal uint BodyId;
        internal float TargetX, TargetY;
        internal long CapturedTicks;
        internal byte[] Pixels; // RGBA, top row first
    }
    [Serializable]
    internal sealed class Result
    {
        public uint frameId;
        public string label;
        public float score, distance;
        public float[] landmarks;
        [NonSerialized] internal Frame Frame;
        [NonSerialized] internal byte[] Preview; // RGBA, bottom row first
    }

    private readonly object gate = new object();
    private readonly AutoResetEvent ready = new AutoResetEvent(false);
    private readonly string python, script, model;
    private Thread thread;
    private Process process;
    private volatile bool stopping;
    private Frame pending;
    private Result latest;
    private string status = "Starting RGB recognition";
    private uint sequence;

    internal MediaPipeHandWorker(string projectRoot)
    {
        python = Path.Combine(projectRoot, ".Local", "MediaPipe", "Scripts", "python.exe");
        model = Path.Combine(projectRoot, ".Local", "MediaPipe", "gesture_recognizer.task");
        script = Path.Combine(projectRoot, "Tools", "MediaPipeHandWorker.py");
    }
    internal string Status { get { lock (gate) return status; } }
    internal Result Latest { get { lock (gate) return latest; } }
    private void SetStatus(string value) { lock (gate) status = value; }
    internal void Start()
    {
        if (!File.Exists(python) || !File.Exists(model) || !File.Exists(script))
        { SetStatus("RGB setup missing: run Tools/SetupMediaPipe.ps1"); return; }
        thread = new Thread(Run) { IsBackground = true, Name = "MediaPipe hand inference" };
        thread.Start();
    }
    internal void Submit(Frame frame)
    {
        if (stopping || thread == null || !thread.IsAlive) return;
        lock (gate) pending = frame;
        ready.Set();
    }
    private void Run()
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = python, Arguments = "-u \"" + script + "\" \"" + model + "\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var child = new Process { StartInfo = info };
            child.ErrorDataReceived += (_, __) => { }; // Drain native logs, never log frames.
            if (!child.Start()) throw new InvalidOperationException("Cannot start RGB worker.");
            lock (gate) process = child;
            child.BeginErrorReadLine();
            using (var output = new BinaryWriter(child.StandardInput.BaseStream))
            {
                while (!stopping)
                {
                    ready.WaitOne();
                    if (stopping) break;
                    Frame frame;
                    lock (gate) { frame = pending; pending = null; }
                    if (frame == null) continue;
                    uint id = ++sequence;
                    output.Write(id); output.Write(frame.Width); output.Write(frame.Height); output.Write(frame.Pixels.Length);
                    output.Write(frame.TargetX); output.Write(frame.TargetY); output.Write(frame.Pixels);
                    output.Flush();
                    string line = child.StandardOutput.ReadLine();
                    if (line == null) throw new IOException("RGB worker stopped unexpectedly.");
                    var result = JsonUtility.FromJson<Result>(line);
                    if (result == null || result.frameId != id) throw new IOException("Invalid RGB worker response.");
                    result.Frame = frame;
                    result.Preview = Preview(frame, result.landmarks);
                    lock (gate) { latest = result; status = "RGB recognition live"; }
                }
            }
        }
        catch (Exception exception)
        {
            if (!stopping) SetStatus("RGB recognition unavailable: " + exception.Message);
        }
        finally
        {
            lock (gate)
            {
                if (process != null)
                {
                    try { if (!process.WaitForExit(1000)) process.Kill(); } catch { }
                    process.Dispose(); process = null;
                }
            }
        }
    }
    private static byte[] Preview(Frame frame, float[] points)
    {
        var pixels = new byte[frame.Pixels.Length];
        int rowBytes = frame.Width * 4;
        for (int y = 0; y < frame.Height; y++)
            Buffer.BlockCopy(frame.Pixels, y * rowBytes, pixels, (frame.Height - 1 - y) * rowBytes, rowBytes);
        if (points == null) return pixels;
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            int x = (int)(points[i] * frame.Width), y = frame.Height - 1 - (int)(points[i + 1] * frame.Height);
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= frame.Width || py >= frame.Height) continue;
                int p = (py * frame.Width + px) * 4;
                pixels[p] = 40; pixels[p + 1] = 255; pixels[p + 2] = 220; pixels[p + 3] = 255;
            }
        }
        return pixels;
    }
    public void Dispose()
    {
        stopping = true;
        lock (gate) { pending = null; latest = null; }
        ready.Set();
        if (thread != null && !thread.Join(3000))
        {
            lock (gate) { try { if (process != null && !process.HasExited) process.Kill(); } catch { } }
            thread.Join(2000);
        }
        // Avoid disposing the wait handle if the worker is still leaving a blocking call.
        if (thread == null || !thread.IsAlive) ready.Dispose();
    }
}
