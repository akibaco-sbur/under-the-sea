using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Vosk;

/// <summary>One owner thread creates, uses and disposes all native objects.</summary>
internal sealed class VoskVoiceSession
{
    internal const int MaxAudioChunks = 32;
    const int MaxResults = 32;
    const int MaxMessages = 32;
    readonly ConcurrentQueue<byte[]> audio = new ConcurrentQueue<byte[]>();
    readonly ConcurrentQueue<string> results = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
    readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    readonly Thread thread;
    readonly string configuredPath, streamingPath, persistentPath, grammar, mutexName, fencePath;
    readonly int sampleRate;
    int audioCount, started;
    volatile bool ready, finished;
    string error;
    volatile bool diagnosticsEnabled;
    long processedBytes;
    string partialJson = "";
    internal bool DiagnosticsEnabled { set => diagnosticsEnabled=value; }
    internal long ProcessedBytes => Interlocked.Read(ref processedBytes);
    internal string PartialJson => Volatile.Read(ref partialJson);
    internal bool Ready => ready && !cancellation.IsCancellationRequested;
    internal bool Finished => finished;
    internal bool HasAudioCapacity => Ready && Volatile.Read(ref audioCount) < MaxAudioChunks;
    internal string Error => Volatile.Read(ref error);

    internal VoskVoiceSession(string configuredPath, string streamingPath, string persistentPath, string grammar, int sampleRate)
    {
        this.configuredPath = configuredPath;
        this.streamingPath = streamingPath;
        this.persistentPath = persistentPath;
        this.grammar = grammar;
        this.sampleRate = sampleRate;
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            mutexName = "UnderTheSea.VoskVoiceSession." + process.Id;
            // The marker survives a managed-domain reload. PID + process start time excludes old processes.
            fencePath = Path.Combine(persistentPath, ".vosk-session-" + process.Id + "-" + process.StartTime.ToUniversalTime().Ticks + ".active");
        }
        thread = new Thread(Run) { IsBackground = true, Name = "Vosk model and recognition owner" };
    }

    internal void Start()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            throw new InvalidOperationException("This Vosk session has already been started. Duplicate start was blocked.");
        try { thread.Start(); }
        catch { Volatile.Write(ref started, 0); throw; }
    }
    internal void RequestStop() => cancellation.Cancel();
    internal bool WaitForStop(int milliseconds) => Volatile.Read(ref started) == 0 || thread.Join(milliseconds);
    internal bool TryReadResult(out string result) => results.TryDequeue(out result);
    internal bool TryReadMessage(out string message) => messages.TryDequeue(out message);

    internal bool TryEnqueue(byte[] pcm)
    {
        if (!Ready || pcm == null || pcm.Length == 0 || pcm.Length > 64 * 1024) return false;
        if (Interlocked.Increment(ref audioCount) > MaxAudioChunks)
        {
            Interlocked.Decrement(ref audioCount);
            return false;
        }
        audio.Enqueue(pcm);
        return true;
    }

    void Report(string message)
    {
        if (messages.Count < MaxMessages) messages.Enqueue(message);
    }

    void Run()
    {
        try
        {
            // OS-level gate also protects against an older managed domain still loading.
            using (var gate = new Mutex(false, mutexName))
            {
                bool ownsGate = false;
                try
                {
                    try { ownsGate = gate.WaitOne(0); }
                    catch (AbandonedMutexException)
                    {
                        ownsGate = true;
                        throw new InvalidOperationException("A previous Vosk worker ended abnormally. Restart Unity before loading another model.");
                    }
                    if (!ownsGate) throw new InvalidOperationException("Another Vosk session is still active/loading/stopping. No duplicate model was loaded.");
                    CancellationToken token = cancellation.Token;
                    token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(persistentPath);
                    if (File.Exists(fencePath))
                        throw new InvalidOperationException("Previous native cleanup could not be confirmed. Restart Unity before loading another model: " + fencePath);
                    // Keep the cross-process cache lock AND the domain-reload fence for the entire
                    // preparation/loading/recognition/disposal lifetime, not just native loading.
                    using (var usageLock = new FileStream(Path.Combine(persistentPath, ".vosk-active-session.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    using (var fence = new FileStream(fencePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    {
                        bool nativeStarted = false, safelyReleased = false;
                        try
                        {
                            Report("Exclusive session acquired; duplicate preparation/loading is blocked until disposal finishes.");
                            string path = VoskModelPreparation.Resolve(configuredPath, streamingPath, persistentPath, token, Report);
                            long modelBytes = VoskModelPreparation.ModelBytes(path, token);
                            // Disk-space protection remains a hard stop; memory estimates are advisory only.
                            VoskModelPreparation.EnsureDiskSpace(persistentPath, VoskModelPreparation.DiskReserve);
                            VoskModelPreparation.EnsureDiskSpace(Environment.SystemDirectory, VoskModelPreparation.DiskReserve);
                            CheckMemory(modelBytes);
                            token.ThrowIfCancellationRequested();
                            Report("Loading one native model: " + path + " (files: " + modelBytes + " bytes).");
                            nativeStarted = true;
                            RunNative(path, token);
                            safelyReleased = true;
                        }
                        finally
                        {
                            fence.Dispose();
                            if (!nativeStarted || safelyReleased) File.Delete(fencePath);
                            else Report("Cleanup could not be confirmed. Restart Unity before another load; guard retained: " + fencePath);
                        }
                    }
                }
                finally
                {
                    if (ownsGate) gate.ReleaseMutex();
                }
            }
        }
        catch (OperationCanceledException) { Report("Session cancelled; owned native objects disposed."); }
        catch (Exception exception) { Volatile.Write(ref error, exception.GetType().Name + ": " + exception.Message); }
        finally
        {
            ready = false;
            while (audio.TryDequeue(out _)) { }
            while (results.TryDequeue(out _)) { }
            Interlocked.Exchange(ref audioCount, 0);
            // Cancellation source stays valid for repeated OnDisable/OnDestroy calls.
            finished = true;
        }
    }

    void RunNative(string path, CancellationToken token)
    {
        Vosk.Vosk.SetLogLevel(-1);
        using (var model = new Model(path))
        {
            VoskRecognizer recognizer = null;
            try
            {
                if (Model.getCPtr(model).Handle == IntPtr.Zero) throw new InvalidOperationException("Vosk returned a null model handle.");
                // Native loading cannot be interrupted. Check stop immediately upon return.
                token.ThrowIfCancellationRequested();
                bool dynamicGraph = !File.Exists(Path.Combine(path,"graph","HCLG.fst")) &&
                    !File.Exists(Path.Combine(path,"HCLG.fst")) &&
                    (File.Exists(Path.Combine(path,"graph","HCLr.fst")) || File.Exists(Path.Combine(path,"HCLr.fst")));
                recognizer = dynamicGraph ? new VoskRecognizer(model, sampleRate, grammar) : new VoskRecognizer(model, sampleRate);
                Report(dynamicGraph ? "Recognition mode: dynamic grammar (configured phrases)." :
                    "Recognition mode: model default graph. This model does not apply the configured phrase grammar; commands are matched after transcription.");
                    if (VoskRecognizer.getCPtr(recognizer).Handle == IntPtr.Zero)
                        throw new InvalidOperationException("Vosk returned a null recognizer handle.");
                    token.ThrowIfCancellationRequested();
                    recognizer.SetWords(false);
                    ready = true;
                    Report("Model loaded; recognizer ready.");
                    var diagnosticTimer=System.Diagnostics.Stopwatch.StartNew();
                    double nextPartial=0;
                    while (!token.IsCancellationRequested)
                    {
                        if (!audio.TryDequeue(out byte[] pcm))
                        {
                            Thread.Sleep(4);
                            continue;
                        }
                        Interlocked.Decrement(ref audioCount);
                        bool finalized=recognizer.AcceptWaveform(pcm, pcm.Length);
                        Interlocked.Add(ref processedBytes,pcm.Length);
                        if (finalized)
                        {
                            string result = recognizer.Result();
                            Volatile.Write(ref partialJson,"");
                            if (!token.IsCancellationRequested && results.Count < MaxResults && result != null && result.Length <= 16384)
                                results.Enqueue(result);
                        }
                        else if(diagnosticsEnabled && diagnosticTimer.Elapsed.TotalSeconds>=nextPartial)
                        {
                            nextPartial=diagnosticTimer.Elapsed.TotalSeconds+.5;
                            string partial=recognizer.PartialResult();
                            Volatile.Write(ref partialJson,partial != null && partial.Length<=2048 ? partial : "");
                        }
                    }
                    // Do not run FinalResult on an expired session.
            }
            catch (OperationCanceledException) { Report("Session cancelled."); }
            catch (ThreadAbortException) { throw; } // Preserve fence if a domain unload interrupts native ownership.
            catch (Exception exception) { Volatile.Write(ref error, exception.GetType().Name + ": " + exception.Message); }
            finally
            {
                // Disposal failures propagate to the fence owner; they must not be mistaken for clean shutdown.
                recognizer?.Dispose();
            }
        }
        Report("Native recognizer and model disposed.");
    }

    void CheckMemory(long modelBytes)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
        if (!GlobalMemoryStatusEx(ref status))
        {
            Report("WARNING: Cannot query Windows memory. Continuing with one exclusively owned model.");
            return;
        }
        string warning = GetMemoryWarning(modelBytes, status.AvailablePhysical, status.AvailablePageFile);
        if (warning != null) Report("WARNING: " + warning);
    }

    internal static string GetMemoryWarning(long modelBytes, ulong availablePhysical, ulong availableCommit)
    {
        // Conservative guard, not a prediction of exact native peak memory usage.
        ulong physicalNeeded = (ulong)Math.Max(512L * 1024 * 1024, checked(modelBytes * 2));
        ulong commitNeeded = (ulong)checked(modelBytes * 3 + 512L * 1024 * 1024);
        if (availablePhysical >= physicalNeeded && availableCommit >= commitNeeded) return null;
        return "Memory is below the advisory estimate. Available physical=" + availablePhysical +
            ", commit=" + availableCommit + "; estimated physical=" + physicalNeeded + ", commit=" + commitNeeded +
            ". Continuing with one exclusively owned model; this estimate is not a Vosk loading requirement.";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
