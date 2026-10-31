using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>First-person presentation for the separate scooping sandbox.</summary>
[DefaultExecutionOrder(-10000)]
public sealed class KinectScoopSandbox : MonoBehaviour
{
    [SerializeField] private KinectScoopPose pose;
    [SerializeField] private Camera diverCamera;
    [SerializeField] private ScoopFeedingInteraction interaction;
    [SerializeField] private KinectArmRigFollower armRig;
    [SerializeField] private Transform handCenter, handWrist, upperArm, forearm, bowl, food;
    [SerializeField] private Transform[] fish, tails;
    [SerializeField] private TextMeshProUGUI guide, status, progressText;
    [SerializeField] private UnityEngine.UI.Image progressFill;
    [Header("Keyboard test input (Kinect is disabled while enabled)")]
    [SerializeField] private bool keyboardTestMode = true;
    [SerializeField, Min(0.01f)] private float keyboardHandSpeed = 0.35f;
    [SerializeField, Min(1f)] private float keyboardFastMultiplier = 2.5f;
    [SerializeField] private Vector3 keyboardStartHandLocal = new Vector3(0.28f, -0.30f, 0.48f);
    [Header("Feeding visuals")]
    [SerializeField] private Color[] foodColorPalette =
    {
        new Color(1f, 0.34f, 0.08f),
        new Color(0.08f, 0.72f, 1f),
        new Color(0.32f, 1f, 0.28f),
        new Color(0.8f, 0.25f, 1f)
    };
    private TMP_FontAsset runtimeFont;
    private Vector3[] swimmingCenters;
    private Vector3 lastHand, lastElbow, lastWrist, lastShoulder;
    private bool hasPose;
    private float swimTime;
    private int meals;
    private Renderer bowlRim;
    private MaterialPropertyBlock rimProperties;
    private Renderer foodRenderer;
    private Renderer[] fishBodyRenderers;
    private MaterialPropertyBlock foodProperties;
    private MaterialPropertyBlock fishProperties;
    private Vector3 foodRestScale;
    private Color currentFoodColor;
    private int feedingTargetIndex = -1;
    private int nextFeedingFishIndex;

    // Settable by deterministic Editor checks; manual keyboard input is controlled separately.
    public bool DiagnosticInput { get; set; }
    public Vector3 DiagnosticHandLocal { get; set; } = new Vector3(0.5f, -0.25f, 0.5f);
    public bool DiagnosticTracked { get; set; } = true;
    public ScoopFeedingInteraction Interaction => interaction;
    public int Meals => meals;

    private void OnEnable()
    {
        if (!keyboardTestMode || pose == null)
            return;

        // This component runs before AzureKinectSkeletonView.OnEnable. Disabling
        // it here prevents loading the native SDK or initializing the sensor.
        AzureKinectSkeletonView skeleton = pose.GetComponent<AzureKinectSkeletonView>();
        if (skeleton != null)
            skeleton.enabled = false;
    }

    private void Start()
    {
        if (keyboardTestMode)
        {
            DiagnosticInput = true;
            DiagnosticTracked = true;
            DiagnosticHandLocal = keyboardStartHandLocal;
        }
        interaction.Configure(handCenter, food, bowl, diverCamera);
        if (armRig != null && armRig.IsReady)
        {
            armRig.SetTargetCamera(diverCamera.transform);
            SetVisible(upperArm, false);
            SetVisible(forearm, false);
        }
        else if (armRig != null)
            Debug.LogWarning("KinectArmRigFollower could not find upper_arm.R, forearm.R and hand.R. Using the sandbox arm display instead.", armRig);
        foodRestScale = food.localScale;
        foodRenderer = food.GetComponent<Renderer>();
        if (foodRenderer == null) foodRenderer = food.GetComponentInChildren<Renderer>();
        foodProperties = new MaterialPropertyBlock();
        fishProperties = new MaterialPropertyBlock();
        fishBodyRenderers = new Renderer[fish.Length];
        for (int i = 0; i < fish.Length; i++)
        {
            fishBodyRenderers[i] = fish[i] != null ? fish[i].GetComponent<Renderer>() : null;
            if (fishBodyRenderers[i] == null && fish[i] != null)
                fishBodyRenderers[i] = fish[i].GetComponentInChildren<Renderer>();
        }
        EnterFeedingWait();
        bowlRim = bowl.Find("Rim")?.GetComponent<Renderer>();
        rimProperties = new MaterialPropertyBlock();
        swimmingCenters = new Vector3[fish.Length];
        for (int i = 0; i < fish.Length; i++) swimmingCenters[i] = fish[i].position;
        runtimeFont = TMP_FontAsset.CreateFontAsset("Yu Gothic", "Regular", 64);
        if (runtimeFont != null)
            foreach (var label in GetComponentsInChildren<TextMeshProUGUI>(true)) label.font = runtimeFont;
        SetRestPose();
    }

    private void SetRestPose()
    {
        var cameraTransform = diverCamera.transform;
        lastShoulder = cameraTransform.TransformPoint(new Vector3(0.29f, -0.32f, -0.14f));
        lastHand = cameraTransform.TransformPoint(new Vector3(0.40f, -0.24f, 0.55f));
        lastWrist = cameraTransform.TransformPoint(new Vector3(0.42f, -0.27f, 0.46f));
        lastElbow = cameraTransform.TransformPoint(new Vector3(0.48f, -0.43f, 0.10f));
        DrawArm();
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.08f);
        if (keyboardTestMode)
            UpdateKeyboardHand(Time.unscaledDeltaTime);

        bool usingDiagnosticInput = DiagnosticInput || keyboardTestMode;
        bool tracked = usingDiagnosticInput ? DiagnosticTracked : pose != null && pose.IsTracked;
        if (tracked)
        {
            if (usingDiagnosticInput)
            {
                lastShoulder = diverCamera.transform.TransformPoint(new Vector3(0.29f, -0.32f, -0.14f));
                lastHand = diverCamera.transform.TransformPoint(DiagnosticHandLocal);
                lastElbow = diverCamera.transform.TransformPoint(new Vector3(0.43f, -0.42f, 0.08f));
                lastWrist = lastHand - (lastHand - lastElbow).normalized * 0.09f;
            }
            else
            {
                lastHand = pose.HandPosition;
                lastWrist = pose.WristPosition;
                lastElbow = pose.ElbowPosition;
                lastShoulder = pose.ShoulderPosition;
            }
            hasPose = true;
        }
        DrawArm();
        if (armRig != null && armRig.IsReady)
        {
            Vector3 handForward = lastWrist - lastElbow;
            Quaternion handRotation = usingDiagnosticInput
                ? Quaternion.LookRotation(handForward.sqrMagnitude > 1e-6f ? handForward.normalized : diverCamera.transform.forward, Vector3.up)
                : pose != null ? pose.HandRotation : Quaternion.identity;
            bool hasHandOrientation = usingDiagnosticInput || (pose != null && pose.HasHandOrientation);
            armRig.ApplyPose(lastShoulder, lastElbow, lastWrist, handRotation, hasHandOrientation, tracked, dt);
        }
        interaction.Tick(lastHand, tracked, dt);
        Swim(dt);
        if (bowlRim != null)
        {
            bool ready = tracked && (interaction.IsTouchingFood || interaction.State == ScoopFeedingState.Armed);
            rimProperties.SetColor("_BaseColor", ready ? new Color(0.20f, 1f, 0.35f) : new Color(0.41f, 0.83f, 0.80f));
            bowlRim.SetPropertyBlock(rimProperties);
        }
        guide.text = interaction.Guide;
        if (!tracked) status.text = hasPose ? "追跡を待っています • 餌はそのまま" : "Kinectの前で右腕を動かしてください";
        else if (keyboardTestMode) status.text = "キーボード操作中  WASD:前後左右  Q/E:上下  F:餌色  Backspace:リセット";
        else if (DiagnosticInput) status.text = "動作確認用の入力";
        else status.text = pose.IsEstimated ? "右手 • 肘と手首から補完" : "右手 • 骨格に追従";
        progressFill.fillAmount = interaction.OfferProgress;
        progressFill.enabled = interaction.OfferProgress > 0f;
        progressFill.rectTransform.localScale = new Vector3(interaction.OfferProgress, 1f, 1f);
        progressText.text = !tracked ? "追跡が戻ると操作できます" : interaction.State == ScoopFeedingState.Held
            ? $"差し出す  {interaction.OfferProgress * 100f:0}%"
            : interaction.State == ScoopFeedingState.Offered ? "どうぞ"
            : interaction.State == ScoopFeedingState.Armed ? $"持ち上げる  {interaction.LiftProgress * 100f:0}%"
            : interaction.PickupHint;
    }

    private void DrawArm()
    {
        // The virtual shoulder remains on the diver's right, behind the eye point.
        Vector3 shoulder = diverCamera.transform.TransformPoint(new Vector3(0.29f, -0.32f, -0.14f));
        Vector3 direction = lastWrist - lastElbow;
        if (direction.sqrMagnitude < 0.0001f) direction = diverCamera.transform.forward;
        direction.Normalize();
        // Keep a cupped, palm-up display pose. Finger pose is not an input requirement.
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction);
        if (up.sqrMagnitude < 0.001f) up = Vector3.ProjectOnPlane(-diverCamera.transform.forward, direction);
        handCenter.position = lastHand;
        handWrist.SetPositionAndRotation(lastHand - direction * 0.075f, Quaternion.LookRotation(direction, up.normalized));
        if (armRig == null || !armRig.IsReady)
        {
            Segment(upperArm, shoulder, lastElbow, 0.085f);
            Segment(forearm, lastElbow, handWrist.position, 0.066f);
        }
    }

    private static void SetVisible(Transform target, bool visible)
    {
        if (target == null) return;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            renderer.enabled = visible;
    }

    private void UpdateKeyboardHand(float dt)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            DiagnosticHandLocal = keyboardStartHandLocal;
            interaction.ResetInteraction();
            EnterFeedingWait();
        }
        if (keyboard.fKey.wasPressedThisFrame && interaction.State == ScoopFeedingState.AwaitEntry)
            EnterFeedingWait();

        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float y = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        Vector3 input = new Vector3(x, y, z);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        float speed = keyboardHandSpeed * (keyboard.leftShiftKey.isPressed ? keyboardFastMultiplier : 1f);
        DiagnosticHandLocal += input * speed * Mathf.Max(0f, dt);
        DiagnosticHandLocal = new Vector3(
            Mathf.Clamp(DiagnosticHandLocal.x, 0.05f, 0.85f),
            Mathf.Clamp(DiagnosticHandLocal.y, -0.65f, 0.25f),
            Mathf.Clamp(DiagnosticHandLocal.z, 0.05f, 1.2f));
    }

    private static void Segment(Transform segment, Vector3 a, Vector3 b, float diameter)
    {
        Vector3 delta = b - a;
        segment.position = (a + b) * 0.5f;
        if (delta.sqrMagnitude > 0.00001f) segment.rotation = Quaternion.FromToRotation(Vector3.up, delta);
        segment.localScale = new Vector3(diameter, delta.magnitude * 0.5f, diameter);
    }

    private void Swim(float dt)
    {
        swimTime += dt;
        bool feeding = interaction.FoodAvailableToFish;
        if (feeding && feedingTargetIndex < 0 && fish.Length > 0)
            feedingTargetIndex = nextFeedingFishIndex % fish.Length;
        else if (!feeding)
            feedingTargetIndex = -1;

        for (int i = 0; i < fish.Length; i++)
        {
            float phase = swimTime * (0.28f + 0.03f * i) + i * 2.4f;
            Vector3 target = swimmingCenters[i] + new Vector3(Mathf.Sin(phase) * 0.8f, Mathf.Sin(phase * 1.7f) * 0.12f, Mathf.Cos(phase) * 0.45f);
            bool approachingFood = feeding && i == feedingTargetIndex;
            if (approachingFood) target = interaction.FoodPosition;
            Vector3 delta = target - fish[i].position;
            if (delta.sqrMagnitude > 0.0001f)
                fish[i].rotation = Quaternion.RotateTowards(fish[i].rotation, Quaternion.LookRotation(delta, Vector3.up), dt * 150f);
            fish[i].position = Vector3.MoveTowards(fish[i].position, target, dt * (approachingFood ? 0.58f : 0.20f));
            tails[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(swimTime * 9f + i) * 24f, 0f);
        }
        if (feeding && feedingTargetIndex >= 0 &&
            Vector3.Distance(fish[feedingTargetIndex].position, interaction.FoodPosition) < 0.13f)
        {
            interaction.ConsumeFood();
            ApplyFoodColorToFish(fishBodyRenderers[feedingTargetIndex]);
            meals++;
            nextFeedingFishIndex = (feedingTargetIndex + 1) % fish.Length;
            feedingTargetIndex = -1;
            EnterFeedingWait();
        }
    }

    /// <summary>Starts a new feeding wait and chooses one food color for that meal.</summary>
    public void EnterFeedingWait()
    {
        if (foodColorPalette == null || foodColorPalette.Length == 0)
            currentFoodColor = new Color(1f, 0.45f, 0.12f);
        else
            currentFoodColor = foodColorPalette[Random.Range(0, foodColorPalette.Length)];

        if (food != null)
            food.localScale = foodRestScale;
        ApplyFoodColor(foodRenderer, currentFoodColor, foodProperties);
    }

    private static void ApplyFoodColor(Renderer target, Color color, MaterialPropertyBlock properties)
    {
        if (target == null || properties == null)
            return;
        target.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        target.SetPropertyBlock(properties);
    }

    private void ApplyFoodColorToFish(Renderer target)
    {
        if (target == null)
            return;
        Color tint = Color.Lerp(Color.white, currentFoodColor, 0.72f);
        ApplyFoodColor(target, tint, fishProperties);
    }

    private void OnDestroy()
    {
        if (runtimeFont != null)
        {
            foreach (var texture in runtimeFont.atlasTextures) if (texture != null) Destroy(texture);
            if (runtimeFont.material != null) Destroy(runtimeFont.material);
            Destroy(runtimeFont);
        }
    }
}
