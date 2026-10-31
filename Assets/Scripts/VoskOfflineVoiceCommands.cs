using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Offline Japanese voice-command input for a Unity Windows build.
/// Add the Vosk Unity/native libraries and Japanese model before using.
/// </summary>
[DisallowMultipleComponent]
public sealed class VoskOfflineVoiceCommands : MonoBehaviour
{
    [Serializable] public sealed class CommandEvent : UnityEvent { }

    private enum Command
    {
        LookUp, LookDown, LookRight, LookLeft, TurnAround,
        MoveForward, Stop, Feeding, Retreat
    }

    [Header("Offline Vosk model")]
    [Tooltip("Model folder under StreamingAssets, or a .zip archive there. ZIP models are extracted once to persistentDataPath.")]
    [SerializeField] private string modelFolderName = "vosk-model-ja-0.22.zip";
    [SerializeField, Range(8000, 48000)] private int requestedMicrophoneRate = 16000;

    [Header("Kinect gate")]
    [SerializeField] private bool palmUpRequiredForFeeding = true;
    [SerializeField] private Vector3 palmNormalLocalAxis = Vector3.up;
    [SerializeField, Range(-1f, 1f)] private float palmUpDotThreshold = 0.55f;
    [Header("Feeding test")]
    [Tooltip("Replace Kinect hand input with keyboard input. Speech recognition stays enabled.")]
    [SerializeField] private bool keyboardFeedingTestMode;
    [Header("Development controls")]
    [Tooltip("矢印/B/I/Space/Rで視点・前進・停止・撤退をテストします。音声操作も併用できます。餌やりのキー試験設定とは独立しています。")]
    [SerializeField, InspectorName("開発用キー操作")] private bool keyboardControlTestMode;
    public bool KeyboardControlTestMode => keyboardControlTestMode;
    [Header("Feeding animation")]
    [Tooltip("Pause the diver Animator during feeding. Kinect/keyboard arm tracking stays active.")]
    [SerializeField, InspectorName("餌やり中は通常アニメーションを停止")]
    private bool pauseDiverAnimationWhileFeeding = true;

    [Header("Command events")]
    [SerializeField] private CommandEvent lookUp = new CommandEvent();
    [SerializeField] private CommandEvent lookDown = new CommandEvent();
    [SerializeField] private CommandEvent lookRight = new CommandEvent();
    [SerializeField] private CommandEvent lookLeft = new CommandEvent();
    [SerializeField] private CommandEvent turnAround = new CommandEvent();
    [SerializeField] private CommandEvent moveForward = new CommandEvent();
    [SerializeField] private CommandEvent stop = new CommandEvent();
    [SerializeField] private CommandEvent feeding = new CommandEvent();

    private const int OutputRate = 16000;
    private const int ChunkFrames = 1024;
    private static readonly Dictionary<string, Command> PhraseMap = BuildPhraseMap();

    [SerializeField, Min(10f)] private float modelLoadTimeoutSeconds = 180f;
    private VoskVoiceSession session;
    private Coroutine startup;
    private float nextStorageCheck;
    private float nextAudioWarning;
    private bool shutdownReported;
    private float[] microphoneBuffer;
    private DiverFeedingFoodController feedingFoodController;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    private int microphoneCursor;
    private bool palmUp;
    private bool loggedFirstAudioChunk;

    public bool IsVoiceReady => isActiveAndEnabled && session != null && session.Ready && microphoneClip != null;
    public bool IsVoiceInitializing => isActiveAndEnabled && (session == null || (!session.Ready && !session.Finished));

    private void Awake()
    {
        feedingFoodController = GetComponent<DiverFeedingFoodController>();
        if (feedingFoodController == null)
            feedingFoodController = gameObject.AddComponent<DiverFeedingFoodController>();
        feedingFoodController.Configure(this, keyboardFeedingTestMode, pauseDiverAnimationWhileFeeding);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
#endif
        startup = StartCoroutine(InitializeVoice());
    }

    private IEnumerator InitializeVoice()
    {
        // Re-enabling must not replace a previous owner that is still shutting down.
        if (session != null && !session.Finished)
        {
            Debug.LogError("[Vosk] Previous session is still stopping. Initialization was blocked; no duplicate model was loaded.", this);
            enabled = false;
            yield break;
        }
        shutdownReported = false;
        microphoneCursor = 0;
        loggedFirstAudioChunk = false;
        nextStorageCheck = 0f;
        nextAudioWarning = 0f;
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogError("[Vosk] No microphone available. Model loading was skipped.", this);
            enabled = false;
            yield break;
        }
        microphoneDevice = Microphone.devices[0];
        if (keyboardFeedingTestMode)
            Debug.Log("[Vosk] Feeding keyboard test mode is active; speech recognition remains enabled.", this);
        try
        {
            session = new VoskVoiceSession(modelFolderName, Application.streamingAssetsPath,
                Application.persistentDataPath, BuildGrammarJson(), OutputRate);
            session.Start();
        }
        catch (Exception exception)
        {
            Debug.LogError("[Vosk] Failed to start session: " + exception.Message, this);
            enabled = false;
            yield break;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!session.Ready && !session.Finished)
        {
            if (timer.Elapsed.TotalSeconds > Mathf.Max(10f, modelLoadTimeoutSeconds))
            {
                Debug.LogError("[Vosk] Model loading timed out. Stop requested; another model cannot start while native loading finishes.", this);
                enabled = false;
                yield break;
            }
            yield return null;
        }
        DrainSessionMessages();
        if (session.Finished)
        {
            Debug.LogError("[Vosk] Initialization stopped: " + (session.Error ?? "cancelled"), this);
            enabled = false;
            yield break;
        }
        try
        {
            microphoneClip = Microphone.Start(microphoneDevice, true, 1, requestedMicrophoneRate);
            if (microphoneClip == null) throw new InvalidOperationException("Microphone could not be started.");
            microphoneBuffer = new float[ChunkFrames * microphoneClip.channels];
            Debug.Log($"[Vosk] Microphone started: device={microphoneDevice}, frequency={microphoneClip.frequency}, channels={microphoneClip.channels}.", this);
        }
        catch (Exception exception)
        {
            Debug.LogError("[Vosk] Microphone initialization failed: " + exception.Message, this);
            enabled = false;
            yield break;
        }
        Debug.Log("[Vosk] Recognizer ready. Grammar: " + BuildGrammarJson(), this);
        startup = null;
    }

    private void DrainSessionMessages()
    {
        if (session == null) return;
        for (int i = 0; i < 8 && session.TryReadMessage(out string message); i++)
        {
            if (message.StartsWith("WARNING: ", StringComparison.Ordinal))
                Debug.LogWarning("[Vosk] " + message.Substring(9), this);
            else
                Debug.Log("[Vosk] " + message, this);
        }
    }

    private void Update()
    {
        if (session == null) return;
        DrainSessionMessages();
        if (session.Finished)
        {
            Debug.LogError("[Vosk] Session stopped: " + (session.Error ?? "cancelled"), this);
            enabled = false;
            return;
        }
        // Also watch reserve while native loading is in progress and during recognition.
        if (Time.realtimeSinceStartup >= nextStorageCheck)
        {
            nextStorageCheck = Time.realtimeSinceStartup + 2f;
            try
            {
                VoskModelPreparation.EnsureDiskSpace(Application.persistentDataPath, VoskModelPreparation.DiskReserve);
                VoskModelPreparation.EnsureDiskSpace(Environment.SystemDirectory, VoskModelPreparation.DiskReserve);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Vosk] Storage guard stopped speech: " + exception.Message, this);
                enabled = false;
                return;
            }
        }
        bool waitingForPlayer = DiverStartupGate.IsBlocked(gameObject.scene);
        if (session.Ready && microphoneClip != null && Microphone.IsRecording(microphoneDevice))
        {
            if (waitingForPlayer) microphoneCursor = Mathf.Max(0, Microphone.GetPosition(microphoneDevice));
            else CaptureAvailableAudio();
        }
        // A command may disable/destroy this component. Do not dispatch the rest afterward.
        for (int i = 0; i < 8 && isActiveAndEnabled && session.TryReadResult(out string resultJson); i++)
        {
            if (waitingForPlayer) continue;
            string phrase = ReadText(resultJson);
            Debug.Log($"[Vosk] Final result: json={resultJson}, text=\"{phrase}\", normalized=\"{Normalize(phrase)}\"", this);
            if (!string.IsNullOrEmpty(phrase)) DispatchPhrase(phrase);
        }
    }

    /// <summary>Call from the Kinect hand tracker whenever palm orientation changes.</summary>
    public void SetPalmFacingUp(bool value) => palmUp = value;

    /// <summary>Update the feeding gate from the Kinect hand joint's sensor-space orientation.</summary>
    public void SetPalmOrientation(Quaternion handRotation, bool tracked)
    {
        Vector3 localNormal = palmNormalLocalAxis.sqrMagnitude > 1e-6f
            ? palmNormalLocalAxis.normalized : Vector3.up;
        palmUp = tracked && Vector3.Dot(handRotation * localNormal, Vector3.up) >= palmUpDotThreshold;
    }

    private void CaptureAvailableAudio()
    {
        int currentPosition = Microphone.GetPosition(microphoneDevice);
        if (currentPosition < 0 || currentPosition == microphoneCursor)
            return;

        int clipFrames = microphoneClip.samples;
        int framesAvailable = currentPosition >= microphoneCursor
            ? currentPosition - microphoneCursor
            : clipFrames - microphoneCursor + currentPosition;

        while (framesAvailable >= ChunkFrames)
        {
            if (!session.HasAudioCapacity)
            {
                // Drop stale audio rather than build an unbounded backlog of commands.
                microphoneCursor = currentPosition;
                if (Time.realtimeSinceStartup >= nextAudioWarning)
                {
                    nextAudioWarning = Time.realtimeSinceStartup + 10f;
                    Debug.LogWarning("[Vosk] Audio queue reached its limit; old input was skipped.", this);
                }
                return;
            }
            if (!microphoneClip.GetData(microphoneBuffer, microphoneCursor))
            {
                Debug.LogError("[Vosk] Microphone read failed; stopping session.", this);
                enabled = false;
                return;
            }
            byte[] pcm = ConvertToPcm16Mono(microphoneBuffer, microphoneClip.channels, microphoneClip.frequency);
            if (session.TryEnqueue(pcm) && !loggedFirstAudioChunk)
            {
                loggedFirstAudioChunk = true;
                Debug.Log($"[Vosk] Microphone audio reached recognizer input (first chunk: {pcm.Length} bytes).", this);
            }

            microphoneCursor = (microphoneCursor + ChunkFrames) % clipFrames;
            framesAvailable -= ChunkFrames;
        }
    }

    private void DispatchPhrase(string recognizedText)
    {
        if (DiverStartupGate.IsBlocked(gameObject.scene)) return;
        string normalized = Normalize(recognizedText);
        if (!PhraseMap.TryGetValue(normalized, out Command command))
        {
            Debug.LogWarning($"[Vosk] No command phrase match: recognized=\"{recognizedText}\", normalized=\"{normalized}\"", this);
            return;
        }

        Debug.Log($"[Vosk] Command matched: {command} (phrase=\"{recognizedText}\").", this);
        if(command != Command.Retreat) DiverAnimationController.NotifyRecognizedCommand(gameObject.scene);
        bool feedingAccepted = command != Command.Feeding ||
            ((keyboardFeedingTestMode || !palmUpRequiredForFeeding || palmUp) &&
             feedingFoodController != null && !feedingFoodController.IsFeedingActive);
        if(feedingAccepted) DiverAudioController.PlayRecognition(gameObject.scene,command==Command.Stop || command==Command.Retreat);

        switch (command)
        {
            case Command.Retreat: DiverRetreatController.Request(gameObject.scene); break;
            case Command.LookUp: InvokeCommand("LookUp", lookUp); break;
            case Command.LookDown: InvokeCommand("LookDown", lookDown); break;
            case Command.LookRight: InvokeCommand("LookRight", lookRight); break;
            case Command.LookLeft: InvokeCommand("LookLeft", lookLeft); break;
            case Command.TurnAround: InvokeCommand("TurnAround", turnAround); break;
            case Command.MoveForward: InvokeCommand("MoveForward", moveForward); break;
            case Command.Stop: InvokeCommand("Stop", stop); break;
            case Command.Feeding:
                if (keyboardFeedingTestMode || !palmUpRequiredForFeeding || palmUp)
                {
                    InvokeCommand("Feeding", feeding);
                    if (!HasConfiguredListener(feeding))
                        feedingFoodController?.EnterFeedingWait();
                }
                else
                    Debug.LogWarning("[Vosk] Feeding command blocked: Kinect palm-up condition is false. Check SetPalmFacingUp().", this);
                break;
        }
    }

    private void InvokeCommand(string commandName, CommandEvent commandEvent)
    {
        if (!HasConfiguredListener(commandEvent))
            Debug.LogWarning($"[Vosk] {commandName} matched, but its UnityEvent has no Inspector listeners.", this);
        else
            Debug.Log($"[Vosk] Invoking {commandName} UnityEvent ({commandEvent.GetPersistentEventCount()} Inspector listener(s)).", this);
        commandEvent?.Invoke();
    }

    private static bool HasConfiguredListener(UnityEngine.Events.UnityEventBase commandEvent)
    {
        if (commandEvent == null) return false;
        for (int i = 0; i < commandEvent.GetPersistentEventCount(); i++)
            if (commandEvent.GetPersistentTarget(i) != null &&
                !string.IsNullOrEmpty(commandEvent.GetPersistentMethodName(i)))
                return true;
        return false;
    }

    private static Dictionary<string, Command> BuildPhraseMap()
    {
        var map = new Dictionary<string, Command>(StringComparer.Ordinal);
        Add(map, Command.LookUp, "上", "上向いて", "上を向いて");
        Add(map, Command.LookDown, "下", "下向いて", "下を向いて");
        Add(map, Command.LookRight, "右", "右向いて", "右を向いて");
        Add(map, Command.LookLeft, "左", "左向いて", "左を向いて");
        Add(map, Command.TurnAround, "後ろ", "後ろ向いて", "後ろを向いて");
        Add(map, Command.MoveForward, "進め", "進んで", "すすんで", "進んでください", "前進", "前へ", "前に進んで", "前へ進んで", "前に進んでください");
        Add(map, Command.Stop, "止まれ", "とまれ", "止まって", "とまって", "止まってください", "停止", "ストップ");
        Add(map, Command.Feeding, "餌やり", "餌をあげる", "餌をあげよう");
        Add(map, Command.Retreat, "撤退", "撤退して", "撤退します");
        return map;
    }

    private static void Add(Dictionary<string, Command> map, Command command, params string[] phrases)
    {
        foreach (string phrase in phrases)
            map[Normalize(phrase)] = command;
    }

    private static string BuildGrammarJson()
    {
        var phrases = new List<string>(PhraseMap.Keys);
        phrases.Sort(StringComparer.Ordinal);
        for (int i = 0; i < phrases.Count; i++)
            phrases[i] = "\"" + phrases[i].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return "[" + string.Join(",", phrases) + "]";
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = text.Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant();
        var result = new System.Text.StringBuilder(text.Length);
        foreach (char character in text)
            if (!char.IsWhiteSpace(character) && !char.IsPunctuation(character) && !char.IsSymbol(character))
                result.Append(character);
        return result.ToString();
    }

    private static string ReadText(string json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        try { return JsonUtility.FromJson<ResultData>(json)?.text ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static byte[] ConvertToPcm16Mono(float[] input, int channels, int inputRate)
    {
        int inputFrames = input.Length / channels;
        int outputFrames = Mathf.FloorToInt(inputFrames * (OutputRate / (float)inputRate));
        byte[] output = new byte[outputFrames * 2];
        for (int i = 0; i < outputFrames; i++)
        {
            float sourceFrame = i * (inputRate / (float)OutputRate);
            int first = Mathf.Min((int)sourceFrame, inputFrames - 1);
            int second = Mathf.Min(first + 1, inputFrames - 1);
            float blend = sourceFrame - first;
            float sample = 0f;
            for (int channel = 0; channel < channels; channel++)
            {
                float a = input[first * channels + channel];
                float b = input[second * channels + channel];
                sample += Mathf.Lerp(a, b, blend) / channels;
            }

            short pcmSample = (short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f);
            output[i * 2] = (byte)(pcmSample & 0xff);
            output[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xff);
        }
        return output;
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
#endif
        Shutdown();
    }

    private void OnDestroy() => Shutdown();
    private void OnApplicationQuit() => Shutdown();

#if UNITY_EDITOR
    private void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) Shutdown();
    }
#endif

    private void Shutdown()
    {
        if (startup != null)
        {
            StopCoroutine(startup);
            startup = null;
        }
        // Never dispose native objects from the main thread or forget a live worker.
        // RequestStop is idempotent; its owner thread always disposes in nested using/finally.
        session?.RequestStop();
        try
        {
            if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
                Microphone.End(microphoneDevice);
        }
        finally
        {
            if (microphoneClip != null) Destroy(microphoneClip);
            microphoneClip = null;
            microphoneBuffer = null;
            microphoneDevice = null;
            microphoneCursor = 0;
        }
        if (session != null && !shutdownReported)
        {
            shutdownReported = true;
            if (session.WaitForStop(200))
            {
                DrainSessionMessages();
                if (session.Error == null)
                    Debug.Log("[Vosk] Session stopped; native resources released.", this);
                else
                    Debug.LogError("[Vosk] Session ended with an error: " + session.Error, this);
            }
            else
                Debug.LogWarning("[Vosk] Stop requested. Native call is still finishing; resources remain owned and duplicate model loading is blocked. They will be disposed when the call returns.", this);
        }
    }

    [Serializable] private sealed class ResultData { public string text; }
}
