using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>White pre-game instructions backed by the existing Azure Kinect depth-body tracker.</summary>
[DisallowMultipleComponent]
public sealed class KinectStartupController : MonoBehaviour
{
    [SerializeField, Min(.5f), InspectorName("認識を確認する秒数")] float stableSeconds = 3f;
    [SerializeField, Range(1f,8f), InspectorName("ロボット視界の起動秒数")] float robotBootSeconds = 3.2f;
    [SerializeField, InspectorName("使用フォント（省略可）")] Font displayFont;
    [SerializeField, Min(.3f), InspectorName("離席・認識喪失で終了するまでの秒数")] float seatLostSeconds = 10f;
    [SerializeField, Min(100f), InspectorName("立ち上がり判定の胸の上昇量（mm）")] float standingRiseMm = 300f;
    KinectSeatPresence seatPresence;
    AzureKinectSkeletonView skeleton;
    DiverFeedingFoodController feeding;
    KinectStartupRecognition recognition;
    Canvas canvas;
    RectTransform projectionContent;
    public float RecognitionProgress=>recognition!=null?recognition.Progress:0;
    public uint RecognizedBodyId=>recognition!=null?recognition.BodyId:0;
    public bool HasExperienceStarted=>completed;
    public bool IsDiagnosticStart {get;private set;}
    public void BeginDiagnosticExperience()
    {
        if(!DiverKinectDiagnostics.IsRecordingScene(gameObject.scene) || completed || booting)return;
        IsDiagnosticStart=true;
        Debug.Log("[PlayerStart] Diagnostic-only recognition bypass. Existing Kinect capture remains active.",this);
        BeginGame(false);
    }
    Text status, substatus, testHint;
    Image progress;
    Font font;
    bool ownsFont, completed;
    float nextFind;
    DiverHudController robotHud;
    bool booting, bootUsesTracking, reportedBootUnavailable;
    float trackingLostAt = -1f;

    void Awake()
    {
        DiverStartupGate.Begin(gameObject.scene);
        recognition = new KinectStartupRecognition(stableSeconds);
        BuildScreen();
    }
    // Wait until all scene Awake methods have applied the existing keyboard-test setting.
    void Start() => FindInput();
    void FindInput()
    {
        foreach (var value in FindObjectsByType<DiverFeedingFoodController>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene) { feeding = value; break; }
        if (feeding && feeding.IsKeyboardTest) return; // Never initialize native SDK in the laptop test mode.
        foreach (var value in FindObjectsByType<AzureKinectSkeletonView>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene && value.isActiveAndEnabled) { skeleton = value; break; }
        if (skeleton) return;
        var input = new GameObject("PlayerAzureKinectInput");
        input.SetActive(false); input.transform.SetParent(transform, false);
        skeleton = input.AddComponent<AzureKinectSkeletonView>();
        skeleton.SetDebugVisualization(false, false);
        input.SetActive(true);
    }
    void Update()
    {
        if(projectionContent && canvas)DiverProjectionLayout.Apply(projectionContent,canvas);
        if (completed) { UpdateSeatPresence(); return; }
        if (booting) { UpdateRobotBoot(); return; }
        if (!feeding && Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1f; FindInput(); }
        bool test = feeding && feeding.IsKeyboardTest;
        testHint.gameObject.SetActive(test);
        if (test)
        {
            status.text = "キーボード試験モード";
            substatus.text = "実機では、Azure Kinectの認識後に自動で始まります";
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) BeginGame(false);
            return;
        }
        var snapshot = skeleton && skeleton.isActiveAndEnabled ? skeleton.LatestSnapshot : null;
        double age = snapshot == null ? double.PositiveInfinity :
            (System.Diagnostics.Stopwatch.GetTimestamp() - snapshot.ReceivedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        bool single = snapshot != null && snapshot.Bodies != null && snapshot.Bodies.Length == 1;
        AzureKinectCapture.Body body = single ? snapshot.Bodies[0] : null;
        bool valid = body != null && KinectStartupBodyValidation.IsUpperBodyValid(body.Joints);
        recognition.Tick(valid, body != null ? body.Id : 0, snapshot != null ? snapshot.FrameNumber : -1, age, Time.unscaledTimeAsDouble);
        progress.rectTransform.sizeDelta = new Vector2(440f * recognition.Progress,6f);
        if (valid && age <= .35)
        {
            status.text = "認識できています";
            substatus.text = "そのまま少しお待ちください";
        }
        else if (snapshot != null && snapshot.Bodies != null && snapshot.Bodies.Length > 1)
        {
            status.text = "お一人で椅子に座ってください";
            substatus.text = "ほかの人は少し離れてお待ちください";
        }
        else if (!skeleton || !skeleton.isActiveAndEnabled || skeleton.SensorFrames == 0)
        {
            status.text = "センサーを準備しています";
            substatus.text = "始まらない場合は、センサーの電源と接続をご確認ください";
        }
        else
        {
            status.text = "頭・肩・胸が見える位置に座ってください";
            substatus.text = "椅子に座ったまま、センサーに正面を向けてください";
        }
        if (recognition.Complete) BeginGame(true);
    }
    void BeginGame(bool tracked)
    {
        if (completed || booting) return;
        foreach (var hud in FindObjectsByType<DiverHudController>(FindObjectsSortMode.None))
            if (hud.gameObject.scene == gameObject.scene && hud.isActiveAndEnabled) { robotHud = hud; break; }
        if (!robotHud)
        {
            status.text = "視界の準備を待っています";
            if (!reportedBootUnavailable)
            {
                reportedBootUnavailable = true;
                Debug.LogError("[PlayerStart] An enabled DiverHudController is required for the robot startup sequence.",this);
            }
            return;
        }
        if (tracked && skeleton)
            foreach (var pose in FindObjectsByType<KinectScoopPose>(FindObjectsSortMode.None))
                if (pose.gameObject.scene == gameObject.scene) pose.SelectBody(recognition.BodyId);
        foreach (var movement in FindObjectsByType<DiverMovementController>(FindObjectsSortMode.None))
            if (movement.gameObject.scene == gameObject.scene) movement.ResetMovement();
        bootUsesTracking = tracked;
        seatPresence = null;
        if (tracked && skeleton)
        {
            var snapshot = skeleton.LatestSnapshot;
            if (snapshot != null && snapshot.Bodies != null)
                foreach (var body in snapshot.Bodies)
                    if (body != null && body.Id == recognition.BodyId && KinectStartupBodyValidation.IsUpperBodyValid(body.Joints))
                    {
                        seatPresence = new KinectSeatPresence(seatLostSeconds, standingRiseMm);
                        seatPresence.Begin(body.Id, body.Joints[2].Y);
                        break;
                    }
        }
        if (tracked && seatPresence == null) return;
        booting = true;
        trackingLostAt = -1f;
        robotHud.BeginRobotBoot(robotBootSeconds);
        canvas.gameObject.SetActive(false);
        Debug.Log(tracked ? "[PlayerStart] Player recognized. Robot optical display is starting; controls remain blocked." :
            IsDiagnosticStart ? "[PlayerStart] Diagnostic bypass. Robot display is starting while Kinect capture continues." :
            "[PlayerStart] Explicit keyboard-test bypass. Starting robot display without Kinect recognition.",this);
    }
    void UpdateRobotBoot()
    {
        bool valid = !bootUsesTracking;
        if (bootUsesTracking && skeleton && skeleton.isActiveAndEnabled)
        {
            var snapshot = skeleton.LatestSnapshot;
            double age = snapshot == null ? double.PositiveInfinity :
                (System.Diagnostics.Stopwatch.GetTimestamp()-snapshot.ReceivedTicks)/(double)System.Diagnostics.Stopwatch.Frequency;
            valid = age>=0 && age<=.35 && snapshot.Bodies!=null && snapshot.Bodies.Length==1 && snapshot.Bodies[0]!=null &&
                snapshot.Bodies[0].Id==recognition.BodyId && KinectStartupBodyValidation.IsUpperBodyValid(snapshot.Bodies[0].Joints);
        }
        if (valid) trackingLostAt = -1f;
        else if (trackingLostAt < 0) trackingLostAt = Time.unscaledTime;
        if (!robotHud || !robotHud.isActiveAndEnabled || (!valid && Time.unscaledTime-trackingLostAt>=.6f))
        {
            CancelRobotBoot();
            recognition.Reset();
            status.text = "もう一度、頭・肩・胸をセンサーに見せてください";
            return;
        }
        if (!robotHud.CanReleaseRobotControls(valid)) return;
        completed = true;
        booting = false;
        DiverStartupGate.Complete(gameObject.scene);
        robotHud.FinishRobotBoot();
        Debug.Log("[PlayerStart] Robot display startup completed. Gameplay controls enabled.",this);
    }
    void UpdateSeatPresence()
    {
        if (!bootUsesTracking || seatPresence == null || DiverStartupGate.IsBlocked(gameObject.scene)) return;
        var snapshot = skeleton && skeleton.isActiveAndEnabled ? skeleton.LatestSnapshot : null;
        double age = snapshot == null ? double.PositiveInfinity :
            (System.Diagnostics.Stopwatch.GetTimestamp()-snapshot.ReceivedTicks)/(double)System.Diagnostics.Stopwatch.Frequency;
        AzureKinectCapture.Body player = null;
        if (age >= 0 && age <= .35 && snapshot.Bodies != null)
            foreach (var body in snapshot.Bodies)
                if (body != null && body.Id == recognition.BodyId) { player = body; break; }
        bool valid = player != null && KinectStartupBodyValidation.IsUpperBodyValid(player.Joints);
        if (!seatPresence.Tick(valid, player != null ? player.Id : 0, valid ? player.Joints[2].Y : 0, Time.unscaledTimeAsDouble)) return;
        Debug.Log("[PlayerStart] Seated player left or tracking was lost. Ending experience.", this);
        DiverRetreatController.Request(gameObject.scene);
    }
    void CancelRobotBoot()
    {
        if (robotHud) robotHud.CancelRobotBoot();
        booting = false;
        if (!completed && recognition != null) recognition.Reset();
        if (canvas) canvas.gameObject.SetActive(isActiveAndEnabled && !completed);
    }
    void OnDisable() { if(booting)CancelRobotBoot(); if (canvas) canvas.gameObject.SetActive(false); }
    void OnEnable() { if (canvas) canvas.gameObject.SetActive(!completed && !booting); }
    void OnDestroy()
    {
        if(booting && robotHud)robotHud.CancelRobotBoot();
        if (canvas) Destroy(canvas.gameObject);
        if (ownsFont && font) Destroy(font);
    }

    void BuildScreen()
    {
        font = displayFont;
        if (!font)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            foreach (string candidate in new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "Noto Sans CJK JP" })
                if (Array.Exists(installed, name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
                { font = Font.CreateDynamicFontFromOSFont(candidate, 28); ownsFont = true; break; }
        }
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("WhiteRecognitionDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var background = root.AddComponent<Image>(); background.color = Color.white; background.raycastTarget = false;
        projectionContent=Box("ProjectionContent",root.transform,Vector2.zero,new Vector2(1920,1080));
        DiverProjectionLayout.Apply(projectionContent,canvas);
        var center = Box("Content", projectionContent, new Vector2(0, 0), new Vector2(1200, 980));
        TextAt(center, "準備  /  プレイヤー認識", 20, 425, 45, new Color(.39f,.51f,.56f));
        TextAt(center, "はじめる前に", 46, 350, 80, new Color(.2f,.3f,.35f));
        TextAt(center, "椅子に座って、準備しましょう", 26, 275, 55, new Color(.39f,.46f,.49f));
        var icon = Box("SeatedPerson", center, new Vector2(0,60), new Vector2(190,240));
        var figure = icon.gameObject.AddComponent<KinectStartupFigure>(); figure.color = new Color(.35f,.68f,.76f); figure.raycastTarget = false;
        TextAt(center, "座ったまま、頭・肩・胸がセンサーに見えるようにしてください", 27, -110, 55, new Color(.25f,.34f,.38f));
        TextAt(center, "認識が安定すると、そのまま海の中へ進みます", 23, -165, 45, new Color(.46f,.51f,.54f));
        status = TextAt(center, "センサーを準備しています", 28, -255, 60, new Color(.19f,.49f,.6f));
        var track = Box("ProgressTrack", center, new Vector2(0,-320), new Vector2(440,6));
        var trackImage = track.gameObject.AddComponent<Image>(); trackImage.color = new Color(.89f,.93f,.94f); trackImage.raycastTarget = false;
        var fill = Box("Progress", track, Vector2.zero, new Vector2(440,6));
        progress = fill.gameObject.AddComponent<Image>(); progress.color = new Color(.35f,.68f,.76f);
        progress.raycastTarget = false;
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0,.5f);
        fill.anchoredPosition = Vector2.zero; fill.sizeDelta = new Vector2(0,6);
        substatus = TextAt(center, "少しお待ちください", 20, -360, 55, new Color(.46f,.51f,.54f));
        testHint = TextAt(center, "テスト環境：Enterキーで認識画面をスキップ", 18, -430, 40, new Color(.55f,.58f,.6f));
        testHint.gameObject.SetActive(false);
    }
    RectTransform Box(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    Text TextAt(Transform parent, string value, int size, float y, float height, Color tint)
    {
        var rect = Box("Instruction",parent,new Vector2(0,y),new Vector2(1160,height));
        var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.text = value;
        text.color = tint; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
    }
}
