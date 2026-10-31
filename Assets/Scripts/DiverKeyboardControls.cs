using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Development controls use the same public actions as the voice commands.</summary>
[DisallowMultipleComponent]
public sealed class DiverKeyboardControls : MonoBehaviour
{
    // Read the Inspector switch directly so changes during Play take effect immediately.
    // A speech/model failure may disable the voice component; keyboard tests still work.
    public bool KeyboardControlsEnabled => isActiveAndEnabled && voice &&
        voice.gameObject.activeInHierarchy && voice.KeyboardControlTestMode;
    public const string ControlHint = "KEY  ↑/↓/←/→: 視点  B: 後ろ  I: 前進  Space: 停止  R: 撤退";
    DiverViewController view;
    DiverMovementController movement;
    VoskOfflineVoiceCommands voice;
    float nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        for (int i=0; i<SceneManager.sceneCount; i++) Ensure(SceneManager.GetSceneAt(i));
    }
    static void OnLoaded(Scene scene, LoadSceneMode mode) => Ensure(scene);
    static void Ensure(Scene scene)
    {
        if (!scene.isLoaded || scene.name != "Main") return;
        foreach (var controls in FindObjectsByType<DiverKeyboardControls>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (controls.gameObject.scene == scene) return;
        var root = new GameObject("DiverKeyboardInput");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<DiverKeyboardControls>();
    }
    void FindSources()
    {
        foreach (var source in FindObjectsByType<VoskOfflineVoiceCommands>(FindObjectsSortMode.None))
            if (source.gameObject.scene == gameObject.scene) { voice=source; break; }
        foreach (var source in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
            if (source.gameObject.scene == gameObject.scene) { view=source; break; }
        foreach (var source in FindObjectsByType<DiverMovementController>(FindObjectsSortMode.None))
            if (source.gameObject.scene == gameObject.scene) { movement=source; break; }
    }
    void Update()
    {
        if (Time.unscaledTime >= nextFind && (!view || !movement || !voice))
        { nextFind=Time.unscaledTime+1f; FindSources(); }
        if (!KeyboardControlsEnabled || DiverStartupGate.IsBlocked(gameObject.scene)) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.rKey.wasPressedThisFrame)
        { DiverAudioController.PlayRecognition(gameObject.scene,true); DiverRetreatController.Request(gameObject.scene); return; }
        if(keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
           keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
           keyboard.bKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
        {
            DiverAudioController.PlayRecognition(gameObject.scene,keyboard.spaceKey.wasPressedThisFrame);
            DiverAnimationController.NotifyRecognizedCommand(gameObject.scene);
        }
        if (view && view.isActiveAndEnabled)
        {
            if (keyboard.bKey.wasPressedThisFrame) view.TurnAround();
            else
            {
                if (keyboard.upArrowKey.wasPressedThisFrame) view.LookUp();
                else if (keyboard.downArrowKey.wasPressedThisFrame) view.LookDown();
                if (keyboard.rightArrowKey.wasPressedThisFrame) view.LookRight();
                else if (keyboard.leftArrowKey.wasPressedThisFrame) view.LookLeft();
            }
        }
        if (movement && movement.isActiveAndEnabled)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) movement.Stop();
            else if (keyboard.iKey.wasPressedThisFrame) movement.MoveForward();
        }
    }
}
