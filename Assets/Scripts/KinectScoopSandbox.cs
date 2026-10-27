using TMPro;
using UnityEngine;

/// <summary>First-person presentation for the separate scooping sandbox.</summary>
public sealed class KinectScoopSandbox : MonoBehaviour
{
    [SerializeField] private KinectScoopPose pose;
    [SerializeField] private Camera diverCamera;
    [SerializeField] private ScoopFeedingInteraction interaction;
    [SerializeField] private Transform handCenter, handWrist, upperArm, forearm, bowl, food;
    [SerializeField] private Transform[] fish, tails;
    [SerializeField] private TextMeshProUGUI guide, status, progressText;
    [SerializeField] private UnityEngine.UI.Image progressFill;
    private TMP_FontAsset runtimeFont;
    private Vector3[] swimmingCenters;
    private Vector3 lastHand, lastElbow, lastWrist;
    private bool hasPose;
    private float swimTime;
    private int meals;

    // Only for deterministic Editor checks. Live input is always the default.
    public bool DiagnosticInput { get; set; }
    public Vector3 DiagnosticHandLocal { get; set; } = new Vector3(0.5f, -0.25f, 0.5f);
    public bool DiagnosticTracked { get; set; } = true;
    public ScoopFeedingInteraction Interaction => interaction;
    public int Meals => meals;

    private void Start()
    {
        interaction.Configure(handCenter, food, bowl, diverCamera);
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
        lastHand = cameraTransform.TransformPoint(new Vector3(0.40f, -0.24f, 0.55f));
        lastWrist = cameraTransform.TransformPoint(new Vector3(0.42f, -0.27f, 0.46f));
        lastElbow = cameraTransform.TransformPoint(new Vector3(0.48f, -0.43f, 0.10f));
        DrawArm();
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.08f);
        bool tracked = DiagnosticInput ? DiagnosticTracked : pose != null && pose.IsTracked;
        if (tracked)
        {
            if (DiagnosticInput)
            {
                lastHand = diverCamera.transform.TransformPoint(DiagnosticHandLocal);
                lastElbow = diverCamera.transform.TransformPoint(new Vector3(0.43f, -0.42f, 0.08f));
                lastWrist = lastHand - (lastHand - lastElbow).normalized * 0.09f;
            }
            else
            {
                lastHand = pose.HandPosition;
                lastWrist = pose.WristPosition;
                lastElbow = pose.ElbowPosition;
            }
            hasPose = true;
        }
        DrawArm();
        interaction.Tick(lastHand, tracked, dt);
        Swim(dt);
        guide.text = interaction.Guide;
        if (!tracked) status.text = hasPose ? "追跡を待っています • 餌はそのまま" : "Kinectの前で右腕を動かしてください";
        else if (DiagnosticInput) status.text = "動作確認用の入力";
        else status.text = pose.IsEstimated ? "右手 • 肘と手首から補完" : "右手 • 骨格に追従";
        progressFill.fillAmount = interaction.OfferProgress;
        progressFill.enabled = interaction.OfferProgress > 0f;
        progressFill.rectTransform.localScale = new Vector3(interaction.OfferProgress, 1f, 1f);
        progressText.text = interaction.State == ScoopFeedingState.Held
            ? $"差し出す  {interaction.OfferProgress * 100f:0}%"
            : interaction.State == ScoopFeedingState.Offered ? "どうぞ" : $"食べた回数  {meals}";
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
        Segment(upperArm, shoulder, lastElbow, 0.085f);
        Segment(forearm, lastElbow, handWrist.position, 0.066f);
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
        for (int i = 0; i < fish.Length; i++)
        {
            float phase = swimTime * (0.28f + 0.03f * i) + i * 2.4f;
            Vector3 target = swimmingCenters[i] + new Vector3(Mathf.Sin(phase) * 0.8f, Mathf.Sin(phase * 1.7f) * 0.12f, Mathf.Cos(phase) * 0.45f);
            if (feeding) target = interaction.FoodPosition + (i == 0 ? Vector3.forward * 0.04f : new Vector3(Mathf.Cos(phase) * 0.28f, 0.05f * i, Mathf.Sin(phase) * 0.25f));
            Vector3 delta = target - fish[i].position;
            if (delta.sqrMagnitude > 0.0001f)
                fish[i].rotation = Quaternion.RotateTowards(fish[i].rotation, Quaternion.LookRotation(delta, Vector3.up), dt * 150f);
            fish[i].position = Vector3.MoveTowards(fish[i].position, target, dt * (feeding ? 0.58f : 0.20f));
            tails[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(swimTime * 9f + i) * 24f, 0f);
        }
        if (feeding && Vector3.Distance(fish[0].position, interaction.FoodPosition) < 0.13f)
        {
            interaction.ConsumeFood();
            meals++;
        }
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
