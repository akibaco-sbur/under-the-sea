using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>RGB and depth right hand recognition diagnostic, independent of feeding.</summary>
public sealed class KinectHandTest : MonoBehaviour
{
    [SerializeField] private AzureKinectSkeletonView skeleton;
    private readonly HandGestureEstimator gesture = new HandGestureEstimator();
    private UnityEngine.UI.RawImage depthView, zoomView, rgbView, indicator;
    private TextMeshProUGUI stateText, metricsText, statusText, zoomLabel;
    private Texture2D depthTexture, rgbTexture, ballTexture;
    private TMP_FontAsset font;
    private long lastFrame = -1;
    private uint bodyId;
    private bool hasBody, hasPosition, moving;
    private Vector3 previousPosition;
    private float previousTime, speed;
    private AzureKinectCapture.Body body;

    private void Start()
    {
        if (skeleton == null) skeleton = GetComponent<AzureKinectSkeletonView>();
        BuildView();
    }

    private void Update()
    {
        var snapshot = skeleton != null ? skeleton.LatestSnapshot : null;
        if (snapshot == null)
        {
            body = null;
            hasBody = hasPosition = false;
            gesture.ResetTracking();
            ShowUnavailable();
            return;
        }
        if (snapshot.FrameNumber == lastFrame) return;
        lastFrame = snapshot.FrameNumber;
        if (snapshot.DepthImage != null) ShowDepth(snapshot.DepthImage);
        body = snapshot.Bodies.Length > 0 ? snapshot.Bodies[0] : null;
        if (body == null)
        {
            hasBody = hasPosition = false;
            gesture.ResetTracking();
            ShowUnavailable();
            return;
        }
        if (!hasBody || bodyId != body.Id)
        {
            bodyId = body.Id; hasBody = true; hasPosition = false;
            gesture.ResetTracking();
        }
        var hand = body.Joints[15];
        var position = new Vector3(hand.X, hand.Y, hand.Z) * 0.001f;
        float now = Time.unscaledTime;
        if (hasPosition)
        {
            float dt = now - previousTime;
            if (dt > 0 && dt < 1)
                speed = Mathf.Lerp(speed, Vector3.Distance(position, previousPosition) / dt, 1f - Mathf.Exp(-8f * dt));
            else speed = 0;
        }
        else { hasPosition = true; speed = 0; }
        previousPosition = position; previousTime = now;
        moving = speed > 0.35f;
        bool valid = body.RightHandRegion != null && body.RightHandArea > 0;
        var depthPose = gesture.Push(body.RightHandArea, now, moving);
        var rgb = skeleton.LatestRgb;
        bool rgbFresh = rgb?.Frame != null && rgb.Frame.BodyId == body.Id &&
            (System.Diagnostics.Stopwatch.GetTimestamp() - rgb.Frame.CapturedTicks) / (double)System.Diagnostics.Stopwatch.Frequency < 0.75;
        bool shapeValid = rgbFresh && rgb.geometryValid;
        bool isClosed = shapeValid && rgb.fist;
        statusText.text = skeleton.TrackingStatus + "  |  " + skeleton.RgbStatus + "\n" + snapshot.RgbInputStatus;
        if (!shapeValid)
        {
            stateText.text = "UNKNOWN\nFREE";
            indicator.color = new Color(0.35f, 0.4f, 0.43f);
        }
        else
        {
            stateText.text = isClosed ? "CLOSED / GRAB" : "NOT FIST\nFREE";
            indicator.color = isClosed ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.75f, 0.76f, 0.77f);
        }
        ShowMetrics(valid, depthPose, rgbFresh ? rgb : null);
        UpdateZoom(snapshot.DepthImage, body.RightHandRegion);
        ShowRgb(rgbFresh ? rgb : null);
    }

    private void ShowMetrics(bool valid, HandGestureEstimator.Pose depthPose, MediaPipeHandWorker.Result rgb)
    {
        var r = body.RightHandRegion;
        bool measured = rgb != null && rgb.geometryValid;
        string reason = !measured ? (rgb == null ? "RGB missing / stale" : rgb.reason) :
            rgb.fist ? "All four fingers curled" : "Four-finger closure not met";
        metricsText.text = "3D FINGER SHAPE\n" +
            $"Curled fingers   {(measured ? rgb.curledFingers.ToString() : "--")} / 4\n" +
            "Finger     Chord ratio / Bend\n" +
            FingerMetric("Index", 0, rgb) + FingerMetric("Middle", 1, rgb) +
            FingerMetric("Ring", 2, rgb) + FingerMetric("Little", 3, rgb) +
            (rgb != null ? $"Each: ratio <= {rgb.maxCurvedRatio:F2}\nOR <= {rgb.maxBentRatio:F2} + bend >= {rgb.minPalmBendDegrees:F0} deg\n" : "Each: --\n") +
            reason + $"\nSource {(rgb != null ? rgb.source : "--")}, hands {(rgb != null ? rgb.handCount.ToString() : "--")}\n" +
            $"Hand match  {(rgb != null && rgb.distance >= 0 ? rgb.distance.ToString("F3") : "--")} / 0.20\n\n" +
            "OLD CATEGORY (comparison)\n" +
            $"{(rgb != null ? rgb.label : "--")}  {(rgb != null ? rgb.score.ToString("P0") : "--")}\n\n" +
            "DEPTH (comparison only)\n" +
            $"State {depthPose}, ratio {(valid ? gesture.Ratio.ToString("P0") : "--")}\n" +
            $"Area {(valid ? body.RightHandArea.ToString("F1") : "--")}, pixels {(r != null ? r.SelectedCount.ToString() : "--")}\n" +
            $"Hand {(r != null ? (r.HandDepth / 1000f).ToString("F2") : "--")} m, speed {speed:F2} m/s";
    }

    private static string FingerMetric(string name, int index, MediaPipeHandWorker.Result rgb)
    {
        if (rgb == null || !rgb.geometryValid || rgb.straightness == null || rgb.palmBendDegrees == null ||
            rgb.straightness.Length <= index || rgb.palmBendDegrees.Length <= index) return name + "   -- / --\n";
        return $"{name}   {rgb.straightness[index]:F2} / {rgb.palmBendDegrees[index]:F0} deg\n";
    }

    private void ShowUnavailable()
    {
        statusText.text = (skeleton != null ? skeleton.TrackingStatus + "  |  " + skeleton.RgbStatus : "No Kinect component");
        stateText.text = "UNKNOWN\nFREE";
        indicator.color = new Color(0.35f, 0.4f, 0.43f);
        metricsText.text = "DECISION METRICS\n\nWaiting for a tracked right hand.";
        zoomLabel.text = "HAND DEPTH / NO HAND";
        zoomView.texture = null; zoomView.enabled = false;
        rgbView.texture = null; rgbView.enabled = false;
        if (skeleton == null || skeleton.LatestSnapshot == null) depthView.texture = null;
    }

    private void ShowRgb(MediaPipeHandWorker.Result result)
    {
        if (result?.Frame == null || result.Preview == null)
        { rgbView.enabled = false; return; }
        var frame = result.Frame;
        if (rgbTexture == null || rgbTexture.width != frame.Width || rgbTexture.height != frame.Height)
        {
            if (rgbTexture != null) Destroy(rgbTexture);
            rgbTexture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }
        rgbTexture.LoadRawTextureData(result.Preview);
        rgbTexture.Apply(false);
        rgbView.texture = rgbTexture; rgbView.uvRect = new Rect(0, 0, 1, 1); rgbView.enabled = true;
    }

    private void ShowDepth(AzureKinectHandDepth.Image image)
    {
        if (depthTexture == null || depthTexture.width != image.Width || depthTexture.height != image.Height)
        {
            if (depthTexture != null) Destroy(depthTexture);
            depthTexture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        depthTexture.LoadRawTextureData(image.Pixels);
        depthTexture.Apply(false);
        depthView.texture = depthTexture;
    }
    private void UpdateZoom(AzureKinectHandDepth.Image image, AzureKinectHandDepth.Region r)
    {
        if (image == null || r == null || depthTexture == null)
        { zoomView.texture = null; zoomView.enabled = false; zoomLabel.text = "HAND DEPTH / NO HAND"; return; }
        zoomView.texture = depthTexture; zoomView.enabled = true;
        zoomView.uvRect = new Rect((r.CenterX - r.RadiusX) / image.Width, (image.Height - r.CenterY - r.RadiusY) / image.Height,
            2 * r.RadiusX / image.Width, 2 * r.RadiusY / image.Height);
        zoomLabel.text = "HAND DEPTH / ZOOM";
    }

    private void BuildView()
    {
        font = TMP_Settings.defaultFontAsset;
        var canvasObject = new GameObject("HandTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        var root = canvasObject.GetComponent<RectTransform>();
        Panel(root, "Background", Vector2.zero, Vector2.one, new Color(0.035f, 0.045f, 0.055f));
        Text(root, "Title", "KINECT HAND TEST", new Vector2(0.02f, 0.91f), new Vector2(0.8f, 0.99f), 40, FontStyles.Bold);
        statusText = Text(root, "Status", "Starting...", new Vector2(0.02f, 0.85f), new Vector2(0.98f, 0.91f), 18);
        Panel(root, "DepthPanel", new Vector2(0.02f, 0.04f), new Vector2(0.46f, 0.85f), new Color(0.08f, 0.10f, 0.12f));
        Text(root, "DepthLabel", "DEPTH VIEW", new Vector2(0.03f, 0.79f), new Vector2(0.44f, 0.84f), 22, FontStyles.Bold);
        depthView = Raw(root, "Depth", new Vector2(0.03f, 0.08f), new Vector2(0.45f, 0.78f));
        Panel(root, "ZoomPanel", new Vector2(0.47f, 0.04f), new Vector2(0.67f, 0.85f), new Color(0.08f, 0.10f, 0.12f));
        zoomLabel = Text(root, "ZoomLabel", "HAND DEPTH / ZOOM", new Vector2(0.48f, 0.79f), new Vector2(0.66f, 0.84f), 20, FontStyles.Bold);
        zoomView = Raw(root, "Zoom", new Vector2(0.48f, 0.59f), new Vector2(0.66f, 0.77f));
        Text(root, "RgbLabel", "RGB / HAND POINTS", new Vector2(0.48f, 0.52f), new Vector2(0.66f, 0.58f), 19, FontStyles.Bold);
        rgbView = Raw(root, "Rgb", new Vector2(0.48f, 0.30f), new Vector2(0.66f, 0.51f));
        Text(root, "ZoomKey", "RGB: cyan hand points\nOrange: right target\nMagenta: other wrist\nThumb is not tested", new Vector2(0.48f, 0.15f), new Vector2(0.66f, 0.28f), 16);
        Panel(root, "StatePanel", new Vector2(0.68f, 0.04f), new Vector2(0.98f, 0.85f), new Color(0.08f, 0.10f, 0.12f));
        Text(root, "StateLabel", "HAND STATE", new Vector2(0.70f, 0.79f), new Vector2(0.96f, 0.84f), 22, FontStyles.Bold);
        indicator = Raw(root, "Indicator", new Vector2(0.71f, 0.66f), new Vector2(0.78f, 0.78f));
        ballTexture = MakeBall(); indicator.texture = ballTexture;
        stateText = Text(root, "State", "UNKNOWN", new Vector2(0.79f, 0.66f), new Vector2(0.96f, 0.78f), 27, FontStyles.Bold);
        metricsText = Text(root, "Metrics", "Waiting...", new Vector2(0.70f, 0.05f), new Vector2(0.96f, 0.64f), 19);
        ShowUnavailable();
    }
    private static RectTransform Place(GameObject go, Transform parent, Vector2 min, Vector2 max)
    {
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static void Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        Place(go, parent, min, max); var img = go.GetComponent<UnityEngine.UI.Image>(); img.color = color; img.raycastTarget = false;
    }
    private static UnityEngine.UI.RawImage Raw(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        Place(go, parent, min, max); var raw = go.GetComponent<UnityEngine.UI.RawImage>(); raw.raycastTarget = false; return raw;
    }
    private TextMeshProUGUI Text(Transform parent, string name, string value, Vector2 min, Vector2 max, int size, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        Place(go, parent, min, max); var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.fontStyle = style; text.color = new Color(0.89f, 0.93f, 0.96f);
        text.alignment = TextAlignmentOptions.TopLeft; text.raycastTarget = false; return text;
    }
    private static Texture2D MakeBall()
    {
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var pixels = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
            float r = dx * dx + dy * dy;
            byte alpha = r <= 1 ? (byte)255 : (byte)0;
            byte shade = (byte)Mathf.Clamp(145 + 90 * Mathf.Sqrt(Mathf.Max(0, 1 - r)) - 35 * dx + 30 * dy, 0, 255);
            pixels[y * 64 + x] = new Color32(shade, shade, shade, alpha);
        }
        tex.SetPixels32(pixels); tex.Apply(); return tex;
    }
    private void OnDestroy()
    {
        if (depthTexture != null) Destroy(depthTexture);
        if (rgbTexture != null) Destroy(rgbTexture);
        if (ballTexture != null) Destroy(ballTexture);

    }
}
