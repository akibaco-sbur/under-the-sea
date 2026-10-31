using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DiverStartupGate
{
    static readonly HashSet<int> startedScenes = new HashSet<int>();
    public static bool IsBlocked(Scene scene) => scene.name == "Main" && !startedScenes.Contains(scene.handle);
    internal static void Begin(Scene scene) => startedScenes.Remove(scene.handle);
    internal static void Complete(Scene scene) => startedScenes.Add(scene.handle);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => startedScenes.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
        SceneManager.sceneUnloaded -= OnUnloaded; SceneManager.sceneUnloaded += OnUnloaded;
    }
    static void OnUnloaded(Scene scene) => startedScenes.Remove(scene.handle);
    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Main") return;
        foreach (var existing in Object.FindObjectsByType<KinectStartupController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (existing.gameObject.scene == scene) return;
        var root = new GameObject("PlayerRecognitionScreen");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<KinectStartupController>();
    }
}
