using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Creates only the independent first-person scooping test scene.</summary>
public static class ScoopSandboxBuilder
{
    public const string ScenePath = "Assets/Scenes/Sandbox/ScoopingTest.unity";
    private const string MaterialFolder = "Assets/Materials/Sandbox/Scooping";

    [MenuItem("Tools/Under The Sea/Create Scooping Test")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before creating the scene.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save or discard your scene edits before creating ScoopingTest.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("ScoopingTest already exists. Open the existing scene; this command does not overwrite it.");
        string handPath = AssetDatabase.FindAssets("WhiteHand t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.Contains("SimpleHands/"));
        var handPrefab = handPath == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(handPath);
        if (handPrefab == null) throw new InvalidOperationException("Import Stylized - Simple Hands (221297), including WhiteHand.prefab, before creating this scene.");
        Folder(MaterialFolder);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("ScoopingTest");
        var camera = Child("DiverCamera", root.transform).AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 1.35f, 0f);
        camera.fieldOfView = 72f;
        camera.nearClipPlane = 0.025f;
        camera.farClipPlane = 40f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.22f, 0.30f);
        camera.gameObject.AddComponent<AudioListener>();
        var sun = Child("UnderwaterLight", root.transform).AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(0.73f, 0.94f, 1f);
        sun.intensity = 1.7f;
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.53f, 0.60f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.065f;
        RenderSettings.fogColor = camera.backgroundColor;

        Material sleeve = Material("DivingSuit", new Color(0.075f, 0.13f, 0.17f));
        Material sand = Material("Sand", new Color(0.42f, 0.52f, 0.43f));
        Material stone = Material("Stone", new Color(0.18f, 0.32f, 0.32f));
        Material tray = Material("Tray", new Color(0.23f, 0.42f, 0.45f));
        Material rim = Material("TrayRim", new Color(0.41f, 0.83f, 0.80f));
        Material feed = Material("Food", new Color(0.35f, 0.17f, 0.065f));
        Material eye = Material("FishEye", new Color(0.01f, 0.025f, 0.03f));
        Material[] fishMaterials = { Material("AmberFish", new Color(1f, 0.63f, 0.14f)), Material("BlueFish", new Color(0.24f, 0.77f, 0.92f)), Material("CoralFish", new Color(1f, 0.41f, 0.30f)) };
        Primitive("Seabed", root.transform, PrimitiveType.Plane, new Vector3(0f, -0.5f, 6f), new Vector3(2.4f, 1f, 2.4f), sand);
        for (int i = 0; i < 12; i++)
        {
            float x = Mathf.Sin(i * 2.39f) * (2f + i * 0.36f);
            Primitive($"Rock{i:00}", root.transform, PrimitiveType.Sphere, new Vector3(x, -0.38f, 2.5f + i * 0.63f), new Vector3(0.6f + i % 3 * 0.3f, 0.45f + i % 2 * 0.25f, 0.75f), stone);
        }
        var bowl = Child("FoodBowl", root.transform).transform;
        bowl.position = camera.transform.TransformPoint(new Vector3(0.28f, -0.30f, 0.48f));
        Primitive("Dish", bowl, PrimitiveType.Cylinder, new Vector3(0f, -0.035f, 0f), new Vector3(0.37f, 0.025f, 0.37f), tray);
        Ring("Rim", bowl, 0.183f, 0.012f, rim);
        var food = Primitive("Food", root.transform, PrimitiveType.Cube, bowl.position + new Vector3(0f, 0.028f, 0f), Vector3.one * 0.055f, feed);

        var handCenter = Child("RightHandCenter", root.transform).transform;
        var handWrist = Child("RightHandWrist", root.transform).transform;
        var hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, scene);
        PrefabUtility.UnpackPrefabInstance(hand, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        hand.name = "RightHandModel";
        hand.transform.SetParent(handWrist, false);
        hand.transform.localScale = Vector3.one * 0.165f;
        hand.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        foreach (var animator in hand.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
        foreach (var bone in hand.GetComponentsInChildren<Transform>())
        {
            string n = bone.name;
            float curl = n.EndsWith("Finger") ? 16f : n.EndsWith("2") ? 22f : n.EndsWith("3") ? 15f : 0f;
            if (n.StartsWith("Thumb")) curl = 8f;
            if (curl != 0f) bone.localRotation *= Quaternion.Euler(curl, 0f, 0f);
        }
        var upper = Primitive("RightUpperArm", root.transform, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, sleeve);
        var lower = Primitive("RightForearm", root.transform, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, sleeve);
        var elbowJoint = Primitive("ElbowJoint", lower, PrimitiveType.Sphere, new Vector3(0f, -1f, 0f), new Vector3(1.06f, 0.15f, 1.06f), sleeve);
        handCenter.position = camera.transform.TransformPoint(new Vector3(0.40f, -0.24f, 0.55f));
        handWrist.position = handCenter.position - Vector3.forward * 0.075f;

        var fishes = new Transform[5];
        var tails = new Transform[5];
        for (int i = 0; i < fishes.Length; i++)
        {
            var fish = Child($"Fish{i + 1:00}", root.transform).transform;
            fish.position = new Vector3(-1.1f + i * 0.55f, 1.1f + Mathf.Sin(i * 1.4f) * 0.25f, 2f + i * 0.45f);
            var material = fishMaterials[i % fishMaterials.Length];
            Primitive("Body", fish, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.16f, 0.22f, 0.38f), material);
            Primitive("LeftEye", fish, PrimitiveType.Sphere, new Vector3(-0.073f, 0.035f, 0.105f), Vector3.one * 0.032f, eye);
            Primitive("RightEye", fish, PrimitiveType.Sphere, new Vector3(0.073f, 0.035f, 0.105f), Vector3.one * 0.032f, eye);
            var tail = Child("Tail", fish).transform;
            tail.localPosition = new Vector3(0f, 0f, -0.17f);
            Primitive("TailFin", tail, PrimitiveType.Sphere, new Vector3(0f, 0f, -0.035f), new Vector3(0.035f, 0.22f, 0.13f), material);
            Primitive("TopFin", fish, PrimitiveType.Sphere, new Vector3(0f, 0.095f, -0.02f), new Vector3(0.02f, 0.13f, 0.18f), material);
            fishes[i] = fish;
            tails[i] = tail;
        }

        var input = Child("KinectArmInput", root.transform);
        var skeleton = input.AddComponent<AzureKinectSkeletonView>();
        Set(skeleton, "renderSkeleton", false);
        Set(skeleton, "showStatusOverlay", false);
        Set(skeleton, "estimateHandShape", false);
        var pose = input.AddComponent<KinectScoopPose>();
        Set(pose, "skeleton", skeleton);
        Set(pose, "targetCamera", camera);
        var interaction = root.AddComponent<ScoopFeedingInteraction>();
        var sandbox = root.AddComponent<KinectScoopSandbox>();
        Set(sandbox, "pose", pose); Set(sandbox, "diverCamera", camera); Set(sandbox, "interaction", interaction);
        Set(sandbox, "handCenter", handCenter); Set(sandbox, "handWrist", handWrist);
        Set(sandbox, "upperArm", upper); Set(sandbox, "forearm", lower);
        Set(sandbox, "bowl", bowl); Set(sandbox, "food", food);
        var so = new SerializedObject(sandbox);
        Array(so, "fish", fishes); Array(so, "tails", tails); so.ApplyModifiedPropertiesWithoutUndo();

        var canvasObject = Child("Instructions", root.transform);
        var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900); scaler.matchWidthOrHeight = 0.5f;
        var top = Text("Title", canvas.transform, "SCOOP / UNDER THE SEA", new Vector2(0.04f, 0.91f), new Vector2(0.7f, 0.97f), 25f);
        top.fontStyle = FontStyles.Bold;
        var status = Text("Tracking", canvas.transform, "Kinect", new Vector2(0.04f, 0.86f), new Vector2(0.8f, 0.91f), 18f);
        var panel = Rect("GuidePanel", canvas.transform, new Vector2(0.04f, 0.04f), new Vector2(0.58f, 0.16f));
        panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.01f, 0.06f, 0.085f, 0.84f);
        var guide = Text("Guide", panel, "", new Vector2(0.04f, 0.43f), new Vector2(0.96f, 0.9f), 29f);
        var progressText = Text("ProgressText", panel, "", new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.43f), 18f);
        var fill = Rect("OfferProgress", canvas.transform, new Vector2(0.04f, 0.025f), new Vector2(0.58f, 0.032f)).gameObject.AddComponent<UnityEngine.UI.Image>();
        fill.color = new Color(0.36f, 0.92f, 0.82f); fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillAmount = 0f; fill.raycastTarget = false;
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        Set(sandbox, "guide", guide); Set(sandbox, "status", status); Set(sandbox, "progressText", progressText); Set(sandbox, "progressFill", fill);
        Folder("Assets/Scenes/Sandbox");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
    }

    private static GameObject Child(string name, Transform parent)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
    private static Transform Primitive(string name, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
    }
    private static void Ring(string name, Transform parent, float radius, float width, Material material)
    {
        var ring = Child(name, parent).AddComponent<LineRenderer>();
        ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 64;
        ring.startWidth = ring.endWidth = width; ring.sharedMaterial = material;
        for (int i = 0; i < 64; i++) { float a = i * Mathf.PI * 2f / 64f; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, -0.004f, Mathf.Sin(a) * radius)); }
    }
    private static Material Material(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.25f);
        return material;
    }
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); string parent = path.Substring(0, split); Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Set(UnityEngine.Object target, string field, bool value)
    { var so = new SerializedObject(target); so.FindProperty(field).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Array(SerializedObject so, string field, Transform[] values)
    { var p = so.FindProperty(field); p.arraySize = values.Length; for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    private static TextMeshProUGUI Text(string name, Transform parent, string content, Vector2 min, Vector2 max, float size)
    {
        var text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset; text.text = content; text.fontSize = size;
        text.color = new Color(0.88f, 0.97f, 0.98f); text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal; return text;
    }
}
