using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A bounded, kinematic swimming study. No physics or Kinect input is needed.</summary>
public sealed class FishFeedingDemo : MonoBehaviour
{
    [SerializeField] private Transform food;
    [SerializeField, Range(2, 40)] private int fishCount = 18;
    [SerializeField] private Vector2 halfExtents = new Vector2(8f, 5f);
    [SerializeField, Min(0.1f)] private float cruiseSpeed = 1.25f;
    [SerializeField, Min(1f)] private float turnDegreesPerSecond = 95f;
    [SerializeField, Min(0.1f)] private float acceleration = 1.2f;
    [SerializeField, Min(0.1f)] private float separationDistance = 0.85f;
    [SerializeField] private Camera interactionCamera;
    private Fish[] fish;
    private Vector3[] positions;
    private Material[] materials;
    private Mesh finMesh;
    private Transform fishRoot, foodHalo;
    private float swimTime;
    private float previousMeanDistance;

    public Transform Food { get => food; set => food = value; }
    public int FishCount => fish == null ? 0 : fish.Length;
    public bool IsFeeding => food != null && food.gameObject.activeInHierarchy;
    public float MeanDistanceToFood => previousMeanDistance;
    public Vector3 GetFishPosition(int index) => transform.TransformPoint(fish[index].Position);
    public Vector3 GetFishVelocity(int index) => transform.TransformDirection(fish[index].Direction) * fish[index].Speed;

    private sealed class Fish
    {
        public Transform Root, Visual, Tail;
        public Vector3 Position, Direction;
        public float Speed, Cruise, Phase, TailPhase, Response, OrbitRadius, OrbitSign, Bank;
    }

    private void Awake()
    {
        halfExtents = new Vector2(Mathf.Max(3f, halfExtents.x), Mathf.Max(3f, halfExtents.y));
        materials = new[] { MakeMaterial(new Color(0.18f, 0.72f, 0.78f)), MakeMaterial(new Color(0.94f, 0.67f, 0.25f)), MakeMaterial(new Color(0.52f, 0.75f, 0.88f)), MakeMaterial(new Color(0.05f, 0.10f, 0.15f)), MakeMaterial(new Color(0.94f, 0.91f, 0.74f)) };
        finMesh = new Mesh { name = "FishFin" };
        finMesh.vertices = new[] { Vector3.zero, new Vector3(-0.23f, 0, -0.3f), new Vector3(0.23f, 0, -0.3f), Vector3.zero, new Vector3(-0.23f, 0, -0.3f), new Vector3(0.23f, 0, -0.3f) };
        finMesh.triangles = new[] { 0, 2, 1, 3, 4, 5 };
        finMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.down, Vector3.down, Vector3.down };
        finMesh.RecalculateBounds();
        fishRoot = new GameObject("SwimmingFish").transform;
        fishRoot.SetParent(transform, false);
        fish = new Fish[Mathf.Clamp(fishCount, 2, 40)];
        positions = new Vector3[fish.Length];
        for (int i = 0; i < fish.Length; i++)
        {
            // Golden-angle placement and deterministic independent traits; no shared random state.
            float phase = i * 2.399963f;
            Vector3 position = new Vector3(Mathf.Cos(phase) * (5f + i % 3), 0, Mathf.Sin(phase) * (3f + i % 2));
            var root = new GameObject($"Fish{i + 1:00}").transform;
            root.SetParent(fishRoot, false);
            var visual = new GameObject("Shape").transform;
            visual.SetParent(root, false);
            visual.localScale = Vector3.one * (0.9f + 0.17f * Mathf.Sin(i * 4.1f));
            Primitive("Body", visual, Vector3.zero, new Vector3(0.32f, 0.24f, 0.76f), materials[i % 3]);
            Primitive("LeftEye", visual, new Vector3(-0.13f, 0.055f, 0.19f), Vector3.one * 0.065f, materials[3]);
            Primitive("RightEye", visual, new Vector3(0.13f, 0.055f, 0.19f), Vector3.one * 0.065f, materials[3]);
            var tail = Fin("Tail", visual, new Vector3(0, 0, -0.3f), materials[i % 3]);
            Fin("LeftFin", visual, new Vector3(-0.1f, 0, 0), materials[4]).localRotation = Quaternion.Euler(0, 75, 0);
            Fin("RightFin", visual, new Vector3(0.1f, 0, 0), materials[4]).localRotation = Quaternion.Euler(0, -75, 0);
            fish[i] = new Fish { Root = root, Visual = visual, Tail = tail, Position = position, Direction = new Vector3(-Mathf.Sin(phase), 0, Mathf.Cos(phase)), Phase = phase, TailPhase = phase, Cruise = Mathf.Max(0.1f, cruiseSpeed) * (0.78f + 0.43f * Fraction(i * 0.618f)), Response = 0.7f + 0.6f * Fraction(i * 0.317f), OrbitRadius = 1.2f + 1.55f * Fraction(i * 0.414f), OrbitSign = i % 4 == 0 ? -1 : 1 };
        }
        if (food != null)
        {
            foodHalo = new GameObject("FoodHalo").transform;
            foodHalo.SetParent(food, false);
            var ring = foodHalo.gameObject.AddComponent<LineRenderer>();
            ring.sharedMaterial = materials[1]; ring.useWorldSpace = false; ring.loop = true;
            ring.positionCount = 48; ring.startWidth = ring.endWidth = 0.035f;
            for (int i = 0; i < ring.positionCount; i++) { float a = i * Mathf.PI * 2 / ring.positionCount; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.55f, 0.02f, Mathf.Sin(a) * 0.55f)); }
        }
        DrawFish();
    }

    private static float Fraction(float value) => value - Mathf.Floor(value);
    private static Material MakeMaterial(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.4f);
        return material;
    }
    private static Transform Primitive(string label, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = new GameObject(label);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        go.AddComponent<MeshRenderer>();
        // Sphere.fbx has radius 1; dimensions here describe the full body/eye size.
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale * 0.5f;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }
    private Transform Fin(string label, Transform parent, Vector3 position, Material material)
    {
        var go = new GameObject(label); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = finMesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go.transform;
    }

    private void Update()
    {
        HandleInput();
        // Discard excess catch-up time: hitches slow simulation instead of teleporting fish.
        float remaining = Mathf.Min(Time.deltaTime, 0.08f);
        while (remaining > 0f) { float step = Mathf.Min(remaining, 1f / 60f); Simulate(step); remaining -= step; }
        DrawFish();
        if (foodHalo != null) foodHalo.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(swimTime * 2.5f));
    }

    private void Simulate(float dt)
    {
        swimTime += dt;
        bool feeding = IsFeeding;
        Vector3 target = feeding ? transform.InverseTransformPoint(food.position) : Vector3.zero;
        target.y = 0;
        for (int i = 0; i < fish.Length; i++) positions[i] = fish[i].Position;
        float totalDistance = 0;
        for (int i = 0; i < fish.Length; i++)
        {
            Fish f = fish[i];
            Vector3 toFood = target - f.Position; toFood.y = 0;
            float distance = toFood.magnitude;
            Vector3 radial = distance > 0.001f ? toFood / distance : f.Direction;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial) * f.OrbitSign;
            float noise = Mathf.Sin(swimTime * (0.62f + f.Response * 0.13f) + f.Phase);
            Vector3 desired;
            float targetSpeed;
            if (feeding)
            {
                float approach = Mathf.Clamp01((distance - f.OrbitRadius) / 2.5f);
                desired = radial * Mathf.Clamp((distance - f.OrbitRadius) * 0.9f, -1.3f, 1.5f) + tangent * (1f - approach * 0.75f);
                desired += Vector3.Cross(Vector3.up, f.Direction) * noise * 0.13f;
                targetSpeed = f.Cruise * Mathf.Lerp(0.43f, 1.35f, approach) * (1f + 0.09f * noise);
            }
            else
            {
                desired = f.Direction + Vector3.Cross(Vector3.up, f.Direction) * noise * 0.85f;
                targetSpeed = f.Cruise * (0.85f + 0.12f * noise);
            }
            Vector3 separation = Vector3.zero;
            float radius = Mathf.Max(0.1f, separationDistance);
            for (int j = 0; j < fish.Length; j++)
            {
                if (i == j) continue;
                Vector3 away = positions[i] - positions[j]; away.y = 0;
                float squared = away.sqrMagnitude;
                if (squared < radius * radius && squared > 0.00001f)
                    separation += away.normalized * (1f - Mathf.Sqrt(squared) / radius);
            }
            desired += separation * 3.5f;
            desired += new Vector3(-Mathf.Sign(f.Position.x) * Mathf.Pow(Mathf.Abs(f.Position.x) / halfExtents.x, 6f), 0, -Mathf.Sign(f.Position.z) * Mathf.Pow(Mathf.Abs(f.Position.z) / halfExtents.y, 6f)) * 1.8f;
            if (desired.sqrMagnitude < 0.0001f) desired = f.Direction;
            float turn = Vector3.SignedAngle(f.Direction, desired, Vector3.up);
            f.Bank = Mathf.MoveTowards(f.Bank, Mathf.Clamp(-turn * 0.35f, -22, 22), 65f * dt);
            f.Direction = Vector3.RotateTowards(f.Direction, desired.normalized, Mathf.Max(1, turnDegreesPerSecond) * Mathf.Deg2Rad * f.Response * dt, 0).normalized;
            f.Speed = Mathf.MoveTowards(f.Speed, targetSpeed, Mathf.Max(0.1f, acceleration) * f.Response * dt);
            f.Position += f.Direction * f.Speed * dt;
            f.TailPhase += dt * (5f + f.Speed * 2f);
            totalDistance += distance;
        }
        previousMeanDistance = totalDistance / fish.Length;
    }

    private void DrawFish()
    {
        foreach (Fish f in fish)
        {
            f.Root.localPosition = f.Position + Vector3.up * (Mathf.Sin(swimTime * 1.3f + f.Phase) * 0.16f + Mathf.Sin(f.Phase) * 0.18f);
            f.Root.localRotation = Quaternion.LookRotation(f.Direction, Vector3.up);
            f.Visual.localRotation = Quaternion.Euler(Mathf.Sin(swimTime * 1.7f + f.Phase) * 3f, 0, f.Bank);
            f.Tail.localRotation = Quaternion.Euler(0, Mathf.Sin(f.TailPhase) * (15f + f.Speed * 8f), 0);
        }
    }
    private void HandleInput()
    {
        if (food == null) return;
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) food.gameObject.SetActive(!food.gameObject.activeSelf);
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || interactionCamera == null) return;
        Vector2 pixel = Mouse.current.position.ReadValue();
        if (pixel.y > Screen.height - 90) return;
        var plane = new Plane(transform.up, transform.position);
        Ray ray = interactionCamera.ScreenPointToRay(pixel);
        if (!plane.Raycast(ray, out float distance)) return;
        Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
        local.x = Mathf.Clamp(local.x, -halfExtents.x + 1, halfExtents.x - 1);
        local.z = Mathf.Clamp(local.z, -halfExtents.y + 1, halfExtents.y - 1); local.y = 0;
        food.position = transform.TransformPoint(local); food.gameObject.SetActive(true);
    }
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(18, 16, 680, 75), GUI.skin.box);
        GUILayout.Label($"FISH FEEDING STUDY / {FishCount} individual swimmers");
        GUILayout.Label("Click water: move food   |   F: food on/off   |   Inspector: move Food");
        GUILayout.Label(IsFeeding ? "FOOD ACTIVE — approaching / circling" : "NO FOOD — free swimming");
        GUILayout.EndArea();
    }
    private void OnDestroy()
    {
        if (fishRoot != null) Destroy(fishRoot.gameObject);
        if (foodHalo != null) Destroy(foodHalo.gameObject);
        if (materials != null) foreach (var material in materials) Destroy(material);
        if (finMesh != null) Destroy(finMesh);
    }
}
