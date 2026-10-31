using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Quiet helmet display; reads existing controls without initializing any input device.</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class DiverHudController : MonoBehaviour
{
    [SerializeField, InspectorName("HUDを表示")] bool showHud = true;
    [SerializeField, Range(0.2f, 1f), InspectorName("表示の濃さ")] float opacity = 0.9f;
    [SerializeField, InspectorName("使用フォント（省略可）")] Font displayFont;
    [SerializeField, InspectorName("境界付近のノイズ演出")] bool showBorderInterference = true;
    [SerializeField, Range(0,1), InspectorName("境界ノイズの強さ")] float borderNoiseStrength = 1f;
    [SerializeField, Min(1f), InspectorName("境界ノイズが始まる距離（m）")] float borderNoiseStartDistance = 150f;
    [SerializeField, Min(0f), InspectorName("境界ノイズが最大になる距離（m）")] float borderNoiseFullDistance = 20f;
    Text borderSignalLabel;
    DiverBorderInterference interference;
    SeaBedWorldBorder worldBorder;
    readonly Color cyan = new Color(0.15f, 0.95f, 1f, 1f);
    readonly Color white = new Color(0.86f, 0.97f, 0.98f, 1f);
    readonly Color muted = new Color(0.43f, 0.68f, 0.72f, 0.85f);
    readonly Color amber = new Color(1f, 0.72f, 0.3f, 1f);
    Canvas canvas;
    CanvasGroup group;
    RectTransform safeRoot, feedingPanel;
    RectTransform obstaclePanel;
    Text obstacleMessage;
    readonly Color warningYellow = new Color(1f,.85f,.18f,1f);
    Text voiceLabel, voiceHint, movementLabel, speedLabel, headingLabel, feedingLabel, normalHint, testHint;
    Image voiceDot, foodSwatch, progress;
    DiverHudCompassGraphic compass;
    VoskOfflineVoiceCommands voice;
    DiverMovementController movement;
    DiverFeedingFoodController feeding;
    Camera view;
    Font font;
    bool ownsFont;
    Rect previousSafeArea;
    Vector2 previousScreen;
    float heading, headingVelocity, nextReadout, nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) EnsureHud(SceneManager.GetSceneAt(i));
    }
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureHud(scene);
    static void EnsureHud(Scene scene)
    {
        if (!scene.isLoaded || scene.name != "Main") return;
        foreach (var hud in FindObjectsByType<DiverHudController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (hud.gameObject.scene == scene) return;
        var root = new GameObject("DiverHelmetHud");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<DiverHudController>();
    }

    CanvasGroup voiceGroup, motionGroup, headingGroup, detailGroup, frameGroup, normalGuideGroup, testGuideGroup;
    RectTransform upperShutter, lowerShutter;
    Image bootShade, bootBar, speedBar;
    Text bootCaption, pitchLabel, armLabel, depthLabel, uptimeLabel;
    GameObject bootTrack;
    RobotHudBootSequence boot;
    KinectScoopPose handPose;
    DiverViewController diverView;
    DiverRetreatController route;
    DiverKeyboardControls keyboardControls;
    float operationStartedAt;
    bool shuttingDown;
    float shutdownProgress;
    public void SetShutdownProgress(float progress)
    {
        shuttingDown = true;
        shutdownProgress = Mathf.Clamp01(progress);
    }
    public bool IsRobotBootComplete => boot != null && boot.Complete;
    public bool CanReleaseRobotControls(bool playerValid) => boot != null && boot.CanReleaseControls(playerValid);

    public void BeginRobotBoot(float seconds)
    {
        boot = new RobotHudBootSequence(seconds);
        canvas.gameObject.SetActive(true);
        ApplyRobotBoot();
    }
    public void FinishRobotBoot()
    {
        boot = null;
        operationStartedAt = Time.unscaledTime;
        ApplyRobotBoot();
    }
    public void CancelRobotBoot() { boot = null; ApplyRobotBoot(); }

    void Awake()
    {
        font = displayFont;
        if (!font)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            foreach (string candidate in new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "Noto Sans CJK JP", "Hiragino Sans" })
                if (Array.Exists(installed, value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)))
                { font = Font.CreateDynamicFontFromOSFont(candidate, 24); ownsFont = true; break; }
        }
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildDisplay();
        FindSources();
    }
    void OnEnable() { if (canvas) canvas.gameObject.SetActive(showHud); }
    void OnDisable()
    {
        interference?.Reset();
        if (canvas) canvas.gameObject.SetActive(false);
        if (feeding) feeding.HudOwnsGuide = false;
    }
    void OnDestroy()
    {
        interference?.Dispose();
        if (feeding) feeding.HudOwnsGuide = false;
        if (canvas) Destroy(canvas.gameObject);
        if (ownsFont && font) Destroy(font);
    }

    void FindSources()
    {
        foreach (var border in FindObjectsByType<SeaBedWorldBorder>(FindObjectsSortMode.None))
            if (border.gameObject.scene == gameObject.scene) { worldBorder = border; break; }
        foreach (var controls in FindObjectsByType<DiverKeyboardControls>(FindObjectsSortMode.None))
            if (controls.gameObject.scene == gameObject.scene) { keyboardControls = controls; break; }
        foreach (var diver in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
            if (diver.gameObject.scene == gameObject.scene && diver.ViewCamera) { view = diver.ViewCamera; diverView = diver; break; }
        foreach (var value in FindObjectsByType<VoskOfflineVoiceCommands>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene) { voice = value; break; }
        foreach (var value in FindObjectsByType<DiverMovementController>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene) { movement = value; break; }
        foreach (var value in FindObjectsByType<DiverFeedingFoodController>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene) { feeding = value; break; }
    }

    void FindHandPose()
    {
        foreach (var value in FindObjectsByType<KinectScoopPose>(FindObjectsSortMode.None))
            if (value.gameObject.scene == gameObject.scene) { handPose = value; break; }
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            if (!view || !voice || !movement || !feeding || !keyboardControls || !worldBorder) FindSources();
            if (!handPose) FindHandPose();
        }
        bool visible = (showHud && !DiverStartupGate.IsBlocked(gameObject.scene)) || boot != null || shuttingDown;
        if (feeding) feeding.HudOwnsGuide = visible;
        if (canvas.gameObject.activeSelf != visible) canvas.gameObject.SetActive(visible);
        if (!visible) { interference?.Reset(); return; }
        if (boot != null) boot.Step(Time.unscaledDeltaTime);
        group.alpha = shuttingDown ? opacity * (1f-shutdownProgress) : boot != null ? 1f : opacity;
        ApplyRobotBoot();
        ApplySafeArea();
        if (boot == null && !shuttingDown && !DiverStartupGate.IsBlocked(gameObject.scene))
        {
            float distance = worldBorder && worldBorder.IsReady && view ? worldBorder.NearestEdgeDistance(view.transform.position) : float.PositiveInfinity;
            float strength = showBorderInterference ? BorderInterferenceState.Proximity(distance,borderNoiseStartDistance,borderNoiseFullDistance) * borderNoiseStrength : 0f;
            borderSignalLabel.gameObject.SetActive(strength > 0f);
            if(strength > 0f) borderSignalLabel.text = "境界まで " + distance.ToString("0.0") + " m   /   SIGNAL INTERFERENCE";
            interference?.Tick(strength,Time.unscaledDeltaTime);
        }
        else { interference?.Reset(); borderSignalLabel.gameObject.SetActive(false); }
        if (view)
        {
            float target = DiverHudReadout.Heading(view.transform.forward, heading);
            heading = Mathf.Repeat(Mathf.SmoothDampAngle(heading, target, ref headingVelocity, 0.12f,
                Mathf.Infinity, Mathf.Min(Time.unscaledDeltaTime, 0.1f)), 360f);
            compass.SetHeading(heading);
        }
        if (!route)
            foreach (var value in FindObjectsByType<DiverRetreatController>(FindObjectsSortMode.None))
                if (value.gameObject.scene == gameObject.scene) { route = value; break; }
        if (Time.unscaledTime < nextReadout) return;
        nextReadout = Time.unscaledTime + 0.1f;
        headingLabel.text = view ? DiverHudReadout.HeadingText(heading) : "—   ---°";
        bool ready = voice && voice.IsVoiceReady;
        bool starting = voice && voice.IsVoiceInitializing;
        voiceLabel.text = ready ? "音声操作 OK" : starting ? "音声を準備中" : "音声操作は待機中";
        voiceLabel.color = voiceDot.color = ready ? cyan : starting ? muted : amber;
        voiceHint.text = ready ? "声で視点・移動・餌やりを操作" : starting ? "準備が終わるまでお待ちください" : "音声入力を確認してください";
        bool canMove = movement && movement.isActiveAndEnabled;
        float speed = canMove ? movement.CurrentSpeed : 0f;
        movementLabel.text = canMove && movement.IsAligningToMove ? "向きを調整中" :
            DiverHudReadout.MovementText(canMove, canMove && movement.IsMoving, speed);
        bool obstacleWarning = canMove && movement.IsObstacleWarningActive &&
            boot == null && !shuttingDown && !DiverStartupGate.IsBlocked(gameObject.scene);
        obstaclePanel.gameObject.SetActive(obstacleWarning);
        if (obstacleWarning)
        {
            obstacleMessage.text = movement.IsBorderIdleCountdownActive ?
                "境界で停止中。あと " + Mathf.CeilToInt(movement.BorderIdleSecondsRemaining) + " 秒で自動撤退" :
                speed > .005f ? "衝突回避のため自動ブレーキ中" : "衝突回避のため停止しました";
            movementLabel.text = speed > .005f ? "衝突回避で減速中" : "障害物の手前で停止";
        }
        movementLabel.color = obstacleWarning ? warningYellow : white;
        speedLabel.text = canMove ? speed.ToString("0.00") + "  m/s" : "—  m/s";
        pitchLabel.text = view && diverView ? "視線方向  " +
            DiverHudReadout.ViewDirection(view.transform.forward, diverView.BodyForward) : "視線方向  —";
        depthLabel.text = route ? route.TravelDistance.ToString("0.0") + " m" : "— m";
        armLabel.text = feeding && feeding.IsKeyboardTest ? "キー試験入力" : handPose && handPose.IsTracked ? "腕を追跡中" : "腕の認識を待機中";
        armLabel.color = feeding && feeding.IsKeyboardTest ? muted : handPose && handPose.IsTracked ? cyan : amber;
        speedBar.rectTransform.sizeDelta = new Vector2(264f * (canMove ? movement.SpeedRatio : 0f), 3f);
        int seconds = DiverStartupGate.IsBlocked(gameObject.scene) ? 0 : Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - operationStartedAt));
        uptimeLabel.text = "稼働時間  " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        bool active = feeding && feeding.IsFeedingActive;
        feedingPanel.gameObject.SetActive(active);
        normalHint.gameObject.SetActive(!active);
        bool hasKeyboard = keyboardControls && keyboardControls.KeyboardControlsEnabled;
        bool feedingTest = feeding && feeding.IsKeyboardTest;
        testHint.gameObject.SetActive(hasKeyboard || feedingTest);
        testHint.text = (hasKeyboard ? DiverKeyboardControls.ControlHint : "") +
            (feedingTest ? "   F: 餌やり  WASD: 手の移動  E/Q: 上下  Shift: 速く  Backspace: 餌やり終了" : "");
        if (active)
        {
            feedingLabel.text = feeding.FeedingGuide;
            foodSwatch.gameObject.SetActive(!feeding.IsPreparingToFeed);
            foodSwatch.color = feeding.CurrentFoodColor;
            progress.rectTransform.sizeDelta = new Vector2(556f * Mathf.Clamp01(feeding.FeedingProgress),3f);
        }
    }

    void BuildDisplay()
    {
        var root = new GameObject("HelmetDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        group = root.GetComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false;
        bootShade = Box(root.transform,"OpticalBlackout",Vector2.zero,Vector2.zero,Color.black);
        bootShade.rectTransform.anchorMin=Vector2.zero; bootShade.rectTransform.anchorMax=Vector2.one;
        bootShade.rectTransform.offsetMin=bootShade.rectTransform.offsetMax=Vector2.zero;
        upperShutter = Rect("UpperOpticalShutter",root.transform,new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero,new Vector2(.5f,1));
        upperShutter.gameObject.AddComponent<Image>().color=Color.black; upperShutter.GetComponent<Image>().raycastTarget=false;
        lowerShutter = Rect("LowerOpticalShutter",root.transform,Vector2.zero,new Vector2(1,0),Vector2.zero,Vector2.zero,new Vector2(.5f,0));
        lowerShutter.gameObject.AddComponent<Image>().color=Color.black; lowerShutter.GetComponent<Image>().raycastTarget=false;
        safeRoot = Rect("SafeArea", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        var left = Panel("Voice", new Vector2(0f, 1f), new Vector2(44f, -42f), new Vector2(340f, 124f));
        Label(left, "01   /   VOICE LINK", 13, new Vector2(18f, -14f), new Vector2(300f, 23f), muted);
        voiceDot = Box(left, "ReadyDot", new Vector2(18f, -51f), new Vector2(5f, 19f), cyan);
        voiceLabel = Label(left, "音声を準備中", 24, new Vector2(34f, -43f), new Vector2(288f, 37f), white);
        voiceHint = Label(left, "", 14, new Vector2(18f, -90f), new Vector2(310f, 24f), muted);
        var right = Panel("Movement", new Vector2(1f, 1f), new Vector2(-44f, -42f), new Vector2(300f, 124f));
        Label(right, "02   /   NAVIGATION", 13, new Vector2(18f, -14f), new Vector2(264f, 23f), muted);
        movementLabel = Label(right, "停止中", 23, new Vector2(18f, -44f), new Vector2(264f, 35f), white);
        speedLabel = Label(right, "0.00  m/s", 17, new Vector2(18f, -88f), new Vector2(264f, 25f), cyan);
        voiceGroup = left.gameObject.AddComponent<CanvasGroup>();
        motionGroup = right.gameObject.AddComponent<CanvasGroup>();
        var navigation = Rect("Heading", safeRoot, new Vector2(.5f,1f), new Vector2(.5f,1f), new Vector2(0f,-40f), new Vector2(390f,90f), new Vector2(.5f,1f));
        headingLabel = Label(navigation, "N   000°", 23, new Vector2(0f,-9f), new Vector2(390f,35f), cyan);
        headingLabel.alignment = TextAnchor.UpperCenter;
        var ticks = Rect("CompassScale", navigation, new Vector2(.5f,1f), new Vector2(.5f,1f), new Vector2(0f,-53f), new Vector2(360f,24f), new Vector2(.5f,1f));
        compass = ticks.gameObject.AddComponent<DiverHudCompassGraphic>(); compass.color = cyan; compass.raycastTarget = false;
        headingGroup = navigation.gameObject.AddComponent<CanvasGroup>();
        var frame = Rect("RobotFrame",safeRoot,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Vector2(.5f,.5f));
        var frameGraphic = frame.gameObject.AddComponent<DiverRobotFrameGraphic>(); frameGraphic.color=cyan; frameGraphic.raycastTarget=false;
        frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
        Label(frame,"ROBOT VISION  /  DIVER",14,new Vector2(44,-192),new Vector2(300,24),cyan);
        var details = Rect("AdditionalReadouts",safeRoot,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,new Vector2(.5f,.5f));
        detailGroup = details.gameObject.AddComponent<CanvasGroup>();
        var hands = Panel("Manipulator",new Vector2(0,.5f),new Vector2(44,35),new Vector2(290,106)); hands.SetParent(details,false);
        hands.pivot=new Vector2(0,1); hands.anchoredPosition=new Vector2(44,35);
        Label(hands,"04   /   MANIPULATOR",13,new Vector2(18,-14),new Vector2(250,24),muted);
        armLabel=Label(hands,"腕の認識を待機中",21,new Vector2(18,-48),new Vector2(254,34),cyan);
        var attitude = Panel("Attitude",new Vector2(1,.5f),new Vector2(-44,35),new Vector2(290,154)); attitude.SetParent(details,false);
        attitude.pivot=new Vector2(1,1); attitude.anchoredPosition=new Vector2(-44,35);
        Label(attitude,"05   /   OPTICAL STATUS",13,new Vector2(18,-14),new Vector2(250,24),muted);
        pitchLabel=Label(attitude,"視線方向  正面",20,new Vector2(18,-48),new Vector2(254,30),white);
        Label(attitude,"累計移動距離",13,new Vector2(18,-88),new Vector2(254,24),muted);
        depthLabel=Label(attitude,"0.0 m",21,new Vector2(18,-115),new Vector2(254,30),cyan);
        var uptime=Rect("Uptime",details,new Vector2(1,0),new Vector2(1,0),new Vector2(-44,90),new Vector2(290,30),new Vector2(1,0));
        uptimeLabel=Label(uptime,"稼働時間  00:00",15,Vector2.zero,new Vector2(290,30),cyan);
        Box(right,"SpeedTrack",new Vector2(18,-117),new Vector2(264,3),new Color(cyan.r,cyan.g,cyan.b,.2f));
        speedBar=Box(right,"Speed",new Vector2(18,-117),new Vector2(0,3),cyan);
        feedingPanel = Panel("Feeding", new Vector2(.5f,0f), new Vector2(0f,56f), new Vector2(600f,150f));
        Label(feedingPanel, "03   /   FEEDING", 13, new Vector2(22f,-15f), new Vector2(400f,23f), muted);
        foodSwatch = Box(feedingPanel,"FoodColor",new Vector2(558f,-18f),new Vector2(16f,16f),Color.white);
        feedingLabel = Label(feedingPanel,"餌に手を近づける",25,new Vector2(22f,-52f),new Vector2(556f,40f),white);
        Box(feedingPanel,"ProgressTrack",new Vector2(22f,-118f),new Vector2(556f,3f),new Color(cyan.r,cyan.g,cyan.b,.15f));
        progress = Box(feedingPanel,"Progress",new Vector2(22f,-118f),new Vector2(556f,3f),cyan);
        progress.rectTransform.sizeDelta=new Vector2(0f,3f);
        normalHint = Label(safeRoot,"進め / 止まれ     ・     上 / 下 / 左 / 右 / 後ろ     ・     餌やり / 撤退",15,Vector2.zero,new Vector2(1000f,28f),muted);
        normalHint.rectTransform.anchorMin=normalHint.rectTransform.anchorMax=new Vector2(.5f,0f);
        normalHint.rectTransform.pivot=new Vector2(.5f,0f); normalHint.rectTransform.anchoredPosition=new Vector2(0f,48f); normalHint.alignment=TextAnchor.MiddleCenter;
        testHint=Label(safeRoot,"TEST   F: 餌やり開始   W/A/S/D: 手の移動   E/Q: 上下   Shift: 速く   Backspace: 餌やり終了   R: 撤退",13,Vector2.zero,new Vector2(1780f,22f),muted);
        testHint.rectTransform.anchorMin=testHint.rectTransform.anchorMax=new Vector2(.5f,0f);
        testHint.rectTransform.pivot=new Vector2(.5f,0f); testHint.rectTransform.anchoredPosition=new Vector2(0f,17f); testHint.alignment=TextAnchor.MiddleCenter;
        normalGuideGroup=normalHint.gameObject.AddComponent<CanvasGroup>();
        testGuideGroup=testHint.gameObject.AddComponent<CanvasGroup>();
        obstaclePanel=Panel("ObstacleWarning",new Vector2(.5f,.5f),new Vector2(0,-135),new Vector2(620,96));
        obstaclePanel.GetComponent<Image>().color=new Color(.12f,.10f,.02f,.94f);
        Box(obstaclePanel,"WarningStripe",Vector2.zero,new Vector2(6,96),warningYellow);
        var warningTitle=Label(obstaclePanel,"CAUTION   前方に障害物",24,new Vector2(24,-12),new Vector2(572,34),warningYellow);
        warningTitle.alignment=TextAnchor.MiddleCenter;
        obstacleMessage=Label(obstaclePanel,"衝突回避のため自動ブレーキ中",19,new Vector2(24,-52),new Vector2(572,30),warningYellow);
        obstacleMessage.alignment=TextAnchor.MiddleCenter;
        obstaclePanel.gameObject.SetActive(false);
        var bootTextRoot=Rect("BootCaption",safeRoot,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-92),new Vector2(760,100),new Vector2(.5f,.5f));
        bootCaption=Label(bootTextRoot,"",26,Vector2.zero,new Vector2(760,50),cyan); bootCaption.alignment=TextAnchor.MiddleCenter;
        var track=Rect("BootProgress",safeRoot,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,-140),new Vector2(360,3),new Vector2(.5f,.5f));
        bootTrack=track.gameObject; track.gameObject.AddComponent<Image>().color=new Color(cyan.r,cyan.g,cyan.b,.2f); track.GetComponent<Image>().raycastTarget=false;
        bootBar=Box(track,"Progress",Vector2.zero,new Vector2(0,3),cyan);
        borderSignalLabel=Label(safeRoot,"",16,Vector2.zero,new Vector2(700,28),warningYellow);
        borderSignalLabel.rectTransform.anchorMin=borderSignalLabel.rectTransform.anchorMax=new Vector2(.5f,1);
        borderSignalLabel.rectTransform.pivot=new Vector2(.5f,1);borderSignalLabel.rectTransform.anchoredPosition=new Vector2(0,-140);
        borderSignalLabel.alignment=TextAnchor.MiddleCenter;borderSignalLabel.gameObject.SetActive(false);
        interference=new DiverBorderInterference(canvas.transform,font);
        interference.AddGauge((RectTransform)voiceGroup.transform);
        interference.AddGauge((RectTransform)motionGroup.transform);
        interference.AddGauge((RectTransform)headingGroup.transform);
        interference.AddGauge((RectTransform)detailGroup.transform);
        feedingPanel.gameObject.SetActive(false);
        ApplyRobotBoot();
        ApplySafeArea();
    }

    void ApplyRobotBoot()
    {
        if (!voiceGroup) return;
        voiceGroup.alpha=motionGroup.alpha=boot!=null?boot.SystemsAlpha:1;
        headingGroup.alpha=boot!=null?boot.HeadingAlpha:1;
        detailGroup.alpha=boot!=null?boot.DetailsAlpha:1;
        frameGroup.alpha=boot!=null?boot.FrameAlpha:1;
        normalGuideGroup.alpha=testGuideGroup.alpha=boot!=null?boot.GuideAlpha:1;
        bootShade.gameObject.SetActive(boot!=null); upperShutter.gameObject.SetActive(boot!=null); lowerShutter.gameObject.SetActive(boot!=null);
        bootCaption.gameObject.SetActive(boot!=null); bootTrack.SetActive(boot!=null);
        if(boot==null)return;
        float height=((RectTransform)canvas.transform).rect.height;
        if(height<=0)height=Screen.height/Mathf.Max(.0001f,canvas.scaleFactor);
        upperShutter.sizeDelta=lowerShutter.sizeDelta=new Vector2(0,height*.5f*(1-boot.OpticsOpen));
        bootShade.color=new Color(0,.018f,.025f,1-boot.OpticsOpen);
        bootCaption.text=boot.Caption;
        bootCaption.color=new Color(cyan.r,cyan.g,cyan.b,boot.CaptionAlpha);
        bootBar.rectTransform.sizeDelta=new Vector2(360*boot.Progress,3);
        bootTrack.GetComponent<Image>().color=new Color(cyan.r,cyan.g,cyan.b,.2f*boot.CaptionAlpha);
        bootBar.color=new Color(cyan.r,cyan.g,cyan.b,boot.CaptionAlpha);
    }

    void ApplySafeArea()
    {
        Rect area=Screen.safeArea; var size=new Vector2(Screen.width,Screen.height);
        if (size.x<=0f || size.y<=0f || (previousSafeArea==area && previousScreen==size)) return;
        previousSafeArea=area; previousScreen=size;
        safeRoot.anchorMin=new Vector2(area.xMin/size.x,area.yMin/size.y);
        safeRoot.anchorMax=new Vector2(area.xMax/size.x,area.yMax/size.y);
        safeRoot.offsetMin=safeRoot.offsetMax=Vector2.zero;
    }
    RectTransform Panel(string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect=Rect(name,safeRoot,anchor,anchor,position,size,anchor);
        var background=rect.gameObject.AddComponent<Image>(); background.color=new Color(.008f,.045f,.065f,.76f); background.raycastTarget=false;
        Box(rect,"TopEdge",Vector2.zero,new Vector2(size.x,1f),new Color(cyan.r,cyan.g,cyan.b,.7f));
        Box(rect,"Accent",Vector2.zero,new Vector2(44f,2f),cyan);
        Box(rect,"Corner",Vector2.zero,new Vector2(1f,18f),cyan);
        return rect;
    }
    Image Box(Transform parent,string name,Vector2 position,Vector2 size,Color color)
    {
        var rect=Rect(name,parent,new Vector2(0f,1f),new Vector2(0f,1f),position,size,new Vector2(0f,1f));
        var image=rect.gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image;
    }
    Text Label(Transform parent,string text,int size,Vector2 position,Vector2 dimensions,Color tint)
    {
        var rect=Rect("Label",parent,new Vector2(0f,1f),new Vector2(0f,1f),position,dimensions,new Vector2(0f,1f));
        var label=rect.gameObject.AddComponent<Text>(); label.font=font; label.fontSize=size; label.text=text;
        label.color=tint; label.raycastTarget=false; label.alignment=TextAnchor.UpperLeft;
        label.horizontalOverflow=HorizontalWrapMode.Wrap; label.verticalOverflow=VerticalWrapMode.Overflow;
        return label;
    }
    static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 position,Vector2 size,Vector2 pivot)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
        rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot; rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
    }
}
