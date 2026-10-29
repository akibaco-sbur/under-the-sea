using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Vosk;

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
        MoveForward, Stop, Feeding
    }

    [Header("Offline Vosk model")]
    [Tooltip("Model folder under StreamingAssets, or a .zip archive there. ZIP models are extracted once to persistentDataPath.")]
    [SerializeField] private string modelFolderName = "vosk-model-ja-0.22.zip";
    [SerializeField, Range(8000, 48000)] private int requestedMicrophoneRate = 16000;

    [Header("Kinect gate")]
    [SerializeField] private bool palmUpRequiredForFeeding = true;

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

    private readonly ConcurrentQueue<byte[]> audioQueue = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<string> recognizedQueue = new ConcurrentQueue<string>();
    private Thread recognitionThread;
    private volatile bool keepRunning;
    private Model model;
    private VoskRecognizer recognizer;
    private AudioClip microphoneClip;
    private string microphoneDevice;
    private int microphoneCursor;
    private bool palmUp;
    private bool loggedFirstAudioChunk;

    private IEnumerator Start()
    {
        string modelPath;
        string streamingAssetsPath = Application.streamingAssetsPath;
        string persistentDataPath = Application.persistentDataPath;
        Task<string> resolveModelTask = Task.Run(
            () => ResolveModelPath(modelFolderName, streamingAssetsPath, persistentDataPath));

        if (modelFolderName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            Debug.Log("Vosk model archive is being prepared. First launch may take several minutes.", this);

        while (!resolveModelTask.IsCompleted)
            yield return null;

        if (resolveModelTask.IsFaulted)
        {
            Debug.LogError("Vosk model preparation failed: " + resolveModelTask.Exception?.GetBaseException().Message, this);
            enabled = false;
            yield break;
        }

        modelPath = resolveModelTask.Result;
        Task<Model> loadModelTask = Task.Run(() => new Model(modelPath));
        while (!loadModelTask.IsCompleted)
            yield return null;

        if (loadModelTask.IsFaulted)
        {
            Debug.LogError("Vosk model loading failed: " + loadModelTask.Exception?.GetBaseException().Message, this);
            enabled = false;
            yield break;
        }

        model = loadModelTask.Result;
        Debug.Log("[Vosk] Model loaded successfully from: " + modelPath, this);

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogError("Vosk voice commands: no microphone is available.", this);
            enabled = false;
            yield break;
        }

        microphoneDevice = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneDevice, true, 1, requestedMicrophoneRate);
        if (microphoneClip == null)
        {
            Debug.LogError("Vosk voice commands: microphone could not be started.", this);
            enabled = false;
            yield break;
        }

        Debug.Log($"[Vosk] Microphone started: device={microphoneDevice}, frequency={microphoneClip.frequency}, channels={microphoneClip.channels}.", this);

        try
        {
            Vosk.Vosk.SetLogLevel(-1);
            recognizer = new VoskRecognizer(model, OutputRate, BuildGrammarJson());
            recognizer.SetWords(false);
        }
        catch (Exception exception)
        {
            Debug.LogError("Vosk initialization failed: " + exception.Message, this);
            Microphone.End(microphoneDevice);
            enabled = false;
            yield break;
        }

        keepRunning = true;
        Debug.Log("[Vosk] Recognizer ready. Grammar: " + BuildGrammarJson(), this);
        recognitionThread = new Thread(RecognitionWorker) { IsBackground = true, Name = "Vosk offline recognition" };
        recognitionThread.Start();
    }

    private static string ResolveModelPath(string configuredPath, string streamingAssetsPath, string persistentDataPath)
    {
        string sourcePath = Path.Combine(streamingAssetsPath, configuredPath);
        if (Directory.Exists(sourcePath))
            return ValidateModelDirectory(sourcePath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Model folder or archive was not found under StreamingAssets.", sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model archive must use the .zip extension.");

        string modelName = Path.GetFileNameWithoutExtension(sourcePath);
        string targetPath = Path.Combine(persistentDataPath, modelName);
        if (Directory.Exists(targetPath))
            return ValidateModelDirectory(targetPath);

        string temporaryPath = targetPath + ".extracting-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporaryPath);
        try
        {
            using (FileStream archiveStream = File.OpenRead(sourcePath))
            using (ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, false))
            {
                string commonRoot = FindCommonRoot(archive.Entries);
                string temporaryRoot = Path.GetFullPath(temporaryPath) + Path.DirectorySeparatorChar;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relativePath = entry.FullName.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(commonRoot))
                    {
                        if (relativePath == commonRoot)
                            continue;
                        if (relativePath.StartsWith(commonRoot + "/", StringComparison.Ordinal))
                            relativePath = relativePath.Substring(commonRoot.Length + 1);
                    }

                    if (string.IsNullOrEmpty(relativePath))
                        continue;

                    string outputPath = Path.GetFullPath(Path.Combine(temporaryPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                    if (!outputPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The model archive contains an invalid path: " + entry.FullName);

                    if (relativePath.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(outputPath);
                        continue;
                    }

                    string outputDirectory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    using (Stream input = entry.Open())
                    using (FileStream output = File.Create(outputPath))
                        input.CopyTo(output);
                }
            }

            ValidateModelDirectory(temporaryPath);
            Directory.Move(temporaryPath, targetPath);
            return targetPath;
        }
        catch
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, true);
            throw;
        }
    }

    private static string FindCommonRoot(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        string root = null;
        foreach (ZipArchiveEntry entry in entries)
        {
            string path = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(path))
                continue;

            int separator = path.IndexOf('/');
            if (separator < 0)
                return string.Empty;

            string entryRoot = path.Substring(0, separator);
            if (root == null)
                root = entryRoot;
            else if (!string.Equals(root, entryRoot, StringComparison.Ordinal))
                return string.Empty;
        }

        return root ?? string.Empty;
    }

    private static string ValidateModelDirectory(string path)
    {
        if (!Directory.Exists(Path.Combine(path, "am")) ||
            !Directory.Exists(Path.Combine(path, "conf")) ||
            !Directory.Exists(Path.Combine(path, "graph")))
            throw new InvalidDataException("The model folder must contain am, conf, and graph folders: " + path);
        return path;
    }

    private void Update()
    {
        if (microphoneClip != null && Microphone.IsRecording(microphoneDevice))
            CaptureAvailableAudio();

        while (recognizedQueue.TryDequeue(out string resultJson))
        {
            string phrase = ReadText(resultJson);
            Debug.Log($"[Vosk] Final result: json={resultJson}, text=\"{phrase}\", normalized=\"{Normalize(phrase)}\"", this);
            if (string.IsNullOrEmpty(phrase))
                Debug.Log("[Vosk] No command phrase was recognized in this segment.", this);
            else
                DispatchPhrase(phrase);
        }
    }

    /// <summary>Call from the Kinect hand tracker whenever palm orientation changes.</summary>
    public void SetPalmFacingUp(bool value) => palmUp = value;

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
            float[] interleaved = new float[ChunkFrames * microphoneClip.channels];
            microphoneClip.GetData(interleaved, microphoneCursor);
            byte[] pcm = ConvertToPcm16Mono(interleaved, microphoneClip.channels, microphoneClip.frequency);
            if (pcm.Length > 0)
            {
                if (!loggedFirstAudioChunk)
                {
                    loggedFirstAudioChunk = true;
                    Debug.Log($"[Vosk] Microphone audio reached recognizer input (first chunk: {pcm.Length} bytes).", this);
                }
                audioQueue.Enqueue(pcm);
            }

            microphoneCursor = (microphoneCursor + ChunkFrames) % clipFrames;
            framesAvailable -= ChunkFrames;
        }
    }

    private void RecognitionWorker()
    {
        while (keepRunning)
        {
            if (!audioQueue.TryDequeue(out byte[] pcm))
            {
                Thread.Sleep(4);
                continue;
            }

            try
            {
                if (recognizer.AcceptWaveform(pcm, pcm.Length))
                    recognizedQueue.Enqueue(recognizer.Result());
            }
            catch (Exception exception)
            {
                Debug.LogError("Vosk recognition failed: " + exception.Message);
            }
        }

        try
        {
            string finalResult = recognizer.FinalResult();
            if (!string.IsNullOrEmpty(finalResult))
                recognizedQueue.Enqueue(finalResult);
        }
        catch { /* Shutting down. */ }
    }

    private void DispatchPhrase(string recognizedText)
    {
        string normalized = Normalize(recognizedText);
        if (!PhraseMap.TryGetValue(normalized, out Command command))
        {
            Debug.LogWarning($"[Vosk] No command phrase match: recognized=\"{recognizedText}\", normalized=\"{normalized}\"", this);
            return;
        }

        Debug.Log($"[Vosk] Command matched: {command} (phrase=\"{recognizedText}\").", this);

        switch (command)
        {
            case Command.LookUp: InvokeCommand("LookUp", lookUp); break;
            case Command.LookDown: InvokeCommand("LookDown", lookDown); break;
            case Command.LookRight: InvokeCommand("LookRight", lookRight); break;
            case Command.LookLeft: InvokeCommand("LookLeft", lookLeft); break;
            case Command.TurnAround: InvokeCommand("TurnAround", turnAround); break;
            case Command.MoveForward: InvokeCommand("MoveForward", moveForward); break;
            case Command.Stop: InvokeCommand("Stop", stop); break;
            case Command.Feeding:
                if (!palmUpRequiredForFeeding || palmUp)
                    InvokeCommand("Feeding", feeding);
                else
                    Debug.LogWarning("[Vosk] Feeding command blocked: Kinect palm-up condition is false. Check SetPalmFacingUp().", this);
                break;
        }
    }

    private void InvokeCommand(string commandName, CommandEvent commandEvent)
    {
        if (commandEvent == null || commandEvent.GetPersistentEventCount() == 0)
            Debug.LogWarning($"[Vosk] {commandName} matched, but its UnityEvent has no Inspector listeners.", this);
        else
            Debug.Log($"[Vosk] Invoking {commandName} UnityEvent ({commandEvent.GetPersistentEventCount()} Inspector listener(s)).", this);
        commandEvent?.Invoke();
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

    private void OnDisable() => Shutdown();

    private void OnDestroy() => Shutdown();

    private void Shutdown()
    {
        keepRunning = false;
        while (audioQueue.TryDequeue(out _)) { }
        if (recognitionThread != null && recognitionThread.IsAlive)
            recognitionThread.Join(3000);
        bool workerStopped = recognitionThread == null || !recognitionThread.IsAlive;
        recognitionThread = null;

        if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
            Microphone.End(microphoneDevice);
        microphoneClip = null;

        if (workerStopped)
        {
            recognizer?.Dispose();
            model?.Dispose();
        }
        recognizer = null;
        model = null;
    }

    [Serializable] private sealed class ResultData { public string text; }
}
