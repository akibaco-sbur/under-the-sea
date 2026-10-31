using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Main feeding presentation and right-arm input; it does not recolor or control fish.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class DiverFeedingFoodController : MonoBehaviour
{
    [SerializeField] private Vector3 trayCameraOffset = new Vector3(0.28f, -0.30f, 0.80f);
    [SerializeField] private bool keyboardTestMode;
    [SerializeField, InspectorName("餌やり中は通常アニメーションを停止")]
    private bool pauseDiverAnimationWhileFeeding = true;
    [SerializeField, Min(0.01f)] private float keyboardHandSpeed = 0.35f;
    [SerializeField, Range(8, 128)] private int dispersionParticleCount = 48;
    [SerializeField] private Color[] foodPalette =
    {
        new Color(1f, 0.34f, 0.08f), new Color(0.08f, 0.72f, 1f),
        new Color(0.32f, 1f, 0.28f), new Color(0.8f, 0.25f, 1f)
    };

    private Camera viewCamera;
    private VoskOfflineVoiceCommands voiceCommands;
    private KinectScoopPose pose;
    private ScoopFeedingInteraction interaction;
    private Transform trayRoot, handTarget;
    private GameObject food;
    private Renderer foodRenderer;
    private ParticleSystem dispersion;
    private MaterialPropertyBlock foodProperties;
    private Color currentFoodColor;
    private int currentFoodColorIndex;
    [Header("餌やり前の魚の集まり")]
    [SerializeField, Range(1,12), InspectorName("呼び寄せる魚の数")] private int gatheringFishCount = 4;
    [SerializeField, Min(1.5f), InspectorName("カメラ前方の距離")] private float gatheringDistance = 2.8f;
    [SerializeField, Min(.2f), InspectorName("自由に泳ぐ範囲")] private float gatheringRadius = .9f;
    [SerializeField, Min(1f), InspectorName("近い魚を探す範囲")] private float gatheringSearchRadius = 60f;
    private SummonFish.FeedingGroup feedingGroup;
    private Coroutine feedingAlignment;
    private DiverViewController feedingView;
    private float nextFishSearchTime;
    private bool loggedNoFeedingFish;
    public int CurrentFoodColorIndex => currentFoodColorIndex;
    public bool HudOwnsGuide { get; set; }
    public bool IsPreparingToFeed => feedingAlignment != null;
    public bool IsFeedingActive => isActiveAndEnabled && (IsPreparingToFeed || (trayRoot != null && trayRoot.gameObject.activeInHierarchy));
    public bool IsKeyboardTest => keyboardTestMode;
    public Color CurrentFoodColor => currentFoodColor;
    public float FeedingProgress => IsPreparingToFeed || interaction == null ? 0f : dispersed ? 1f :
        interaction.IsOffering ? 1f : interaction.State == ScoopFeedingState.Armed ? interaction.LiftProgress : 0f;
    public string FeedingGuide => IsPreparingToFeed ? "正面へ体を向けています" : dispersed ? "魚が餌を食べました" : interaction == null ? "準備中です" :
        interaction.IsOffering ? (feedingGroup != null ? "魚が食べに来ています。そのまま差し出す" : "魚を待っています") :
        interaction.State == ScoopFeedingState.Held ? "餌を前へ差し出す" : interaction.Guide;
    private Vector3 lastHandPosition;
    private bool dispersed;
    private float dispersionElapsed;
    private Vector3 keyboardHandLocal;
    private GameObject keyboardHandMarker;
    private Material keyboardHandMarkerMaterial;
    private KinectArmRigFollower armRig;
    private bool armFollowing, hasArmPose;
    private Vector3 armShoulder, armElbow, armWrist;
    private Quaternion armHandRotation = Quaternion.identity;
    private bool hasArmHandOrientation;
    private readonly DiverFeedingAnimationPause animationPause = new DiverFeedingAnimationPause();

    private void Awake()
    {
        viewCamera = Camera.main;
        foreach (var diver in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
            if (diver.gameObject.scene == gameObject.scene && diver.ViewCamera) { viewCamera = diver.ViewCamera; break; }
        BuildFoodPresentation();
    }

    public void Configure(VoskOfflineVoiceCommands commands, bool useKeyboardTestMode, bool pauseAnimationWhileFeeding = true)
    {
        voiceCommands = commands;
        keyboardTestMode = useKeyboardTestMode;
        pauseDiverAnimationWhileFeeding = pauseAnimationWhileFeeding;
        keyboardHandLocal = trayCameraOffset;
        ConnectArmRig();
        if (keyboardTestMode)
            return;

        AzureKinectSkeletonView skeleton = null;
        foreach (var existing in FindObjectsByType<AzureKinectSkeletonView>(FindObjectsSortMode.None))
            if (existing.gameObject.scene == gameObject.scene && existing.isActiveAndEnabled) { skeleton = existing; break; }
        if (!skeleton)
        {
            var inputRoot = new GameObject("MainFeedingKinectInput");
            inputRoot.SetActive(false);
            inputRoot.transform.SetParent(transform, false);
            skeleton = inputRoot.AddComponent<AzureKinectSkeletonView>();
            skeleton.SetDebugVisualization(false, false);
            inputRoot.SetActive(true);
        }
        skeleton.SetDebugVisualization(false, false);
        pose = skeleton.GetComponent<KinectScoopPose>();
        if (!pose) pose = skeleton.gameObject.AddComponent<KinectScoopPose>();
        pose.SetTargetCamera(viewCamera);
    }

    private void BuildFoodPresentation()
    {
        if (viewCamera == null)
        {
            Debug.LogError("Main feeding food cannot start because there is no MainCamera.", this);
            enabled = false;
            return;
        }

        trayRoot = new GameObject("FeedingTrayPresentation").transform;
        trayRoot.SetParent(transform, false);
        trayRoot.gameObject.SetActive(false);

        var tray = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tray.name = "FoodTray";
        tray.transform.SetParent(trayRoot, false);
        tray.transform.localScale = new Vector3(0.38f, 0.025f, 0.38f);
        Destroy(tray.GetComponent<Collider>());
        tray.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Universal Render Pipeline/Lit", new Color(0.25f, 0.43f, 0.44f));

        var rim = new GameObject("FoodTrayRim").AddComponent<LineRenderer>();
        rim.transform.SetParent(trayRoot, false);
        rim.useWorldSpace = false;
        rim.loop = true;
        rim.positionCount = 48;
        rim.startWidth = rim.endWidth = 0.012f;
        rim.sharedMaterial = MakeMaterial("Universal Render Pipeline/Unlit", new Color(0.48f, 0.88f, 0.82f));
        for (int i = 0; i < rim.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / rim.positionCount;
            rim.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.185f, 0.026f, Mathf.Sin(angle) * 0.185f));
        }

        food = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        food.name = "SolidFood";
        food.transform.SetParent(trayRoot, false);
        food.transform.localPosition = new Vector3(0f, 0.055f, 0f);
        food.transform.localScale = Vector3.one * 0.065f;
        Destroy(food.GetComponent<SphereCollider>());
        foodRenderer = food.GetComponent<Renderer>();
        foodRenderer.sharedMaterial = MakeMaterial("Universal Render Pipeline/Lit", Color.white);
        foodProperties = new MaterialPropertyBlock();

        handTarget = new GameObject("FeedingHandContact").transform;
        handTarget.SetParent(transform, false);
        var handCollider = handTarget.gameObject.AddComponent<SphereCollider>();
        handCollider.radius = 0.065f;
        handCollider.isTrigger = true;
        interaction = gameObject.AddComponent<ScoopFeedingInteraction>();

        dispersion = trayRoot.gameObject.AddComponent<ParticleSystem>();
        var main = dispersion.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.034f);
        var emission = dispersion.emission;
        emission.enabled = false;
        var renderer = dispersion.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = MakeMaterial("Universal Render Pipeline/Particles/Unlit", Color.white);
        var colorOverLifetime = dispersion.colorOverLifetime;
        colorOverLifetime.enabled = false;
        var sizeOverLifetime = dispersion.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.75f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.05f)));

        interaction.Configure(handTarget, food.transform, trayRoot, viewCamera);
        keyboardHandMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        keyboardHandMarker.name = "KeyboardHandMarker";
        keyboardHandMarker.transform.SetParent(handTarget, false);
        keyboardHandMarker.transform.localScale = Vector3.one * 0.03f;
        Destroy(keyboardHandMarker.GetComponent<Collider>());
        keyboardHandMarkerMaterial = MakeMaterial("Universal Render Pipeline/Unlit", new Color(0.15f, 1f, 1f));
        keyboardHandMarker.GetComponent<Renderer>().sharedMaterial = keyboardHandMarkerMaterial;
        keyboardHandMarker.SetActive(false);
        trayRoot.gameObject.SetActive(false);
    }

    // Write bones after the Animator and DiverViewController have updated.
    private void LateUpdate()
    {
        if (DiverStartupGate.IsBlocked(gameObject.scene)) return;
        if (viewCamera == null || interaction == null || (!keyboardTestMode && pose == null))
            return;

        bool tracked;
        if (keyboardTestMode)
        {
            UpdateKeyboardTest(Time.unscaledDeltaTime);
            lastHandPosition = viewCamera.transform.TransformPoint(keyboardHandLocal);
            tracked = true;
            // Move the floating upper arm with the keyboard hand, keeping a
            // bent elbow and a forearm of reasonable length at the new tray depth.
            armElbow = lastHandPosition + viewCamera.transform.TransformVector(new Vector3(0.12f, -0.14f, -0.35f));
            armShoulder = armElbow + viewCamera.transform.TransformVector(new Vector3(-0.12f, 0.10f, -0.30f));
            Vector3 direction = (lastHandPosition - armElbow).normalized;
            armWrist = lastHandPosition - direction * 0.09f;
            Vector3 palmUp = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (palmUp.sqrMagnitude < 1e-6f)
                palmUp = Vector3.ProjectOnPlane(-viewCamera.transform.forward, direction);
            armHandRotation = Quaternion.LookRotation(direction, palmUp.normalized);
            hasArmHandOrientation = true;
            hasArmPose = true;
        }
        else
        {
            tracked = pose != null && pose.IsTracked;
            if (tracked)
            {
                lastHandPosition = pose.HandPosition;
                armShoulder = pose.ShoulderPosition;
                armElbow = pose.ElbowPosition;
                armWrist = pose.WristPosition;
                armHandRotation = pose.HandRotation;
                hasArmHandOrientation = pose.HasHandOrientation;
                hasArmPose = true;
            }
        }
        Vector3 contactPosition = lastHandPosition;
        if (armFollowing && armRig != null && hasArmPose &&
            armRig.ApplyPoseAtPalm(armShoulder, armElbow, armWrist, armHandRotation,
                hasArmHandOrientation, true, Mathf.Min(Time.deltaTime, 0.08f), lastHandPosition))
            contactPosition = armRig.PalmCenterPosition;
        // The rendered palm includes the camera-relative calibration offset.
        // Contact, marker and carried food must all use that same world position.
        interaction.Tick(contactPosition, tracked, Mathf.Min(Time.deltaTime, 0.08f));
        keyboardHandMarker.SetActive(keyboardTestMode && trayRoot.gameObject.activeSelf && !dispersed);
        if (voiceCommands != null)
            voiceCommands.SetPalmOrientation(keyboardTestMode ? Quaternion.LookRotation(Vector3.forward, Vector3.up) : pose != null ? pose.HandRotation : Quaternion.identity,
                keyboardTestMode || (tracked && pose != null && pose.HasHandOrientation));

        UpdateFishFeeding();
        if (dispersed && dispersionElapsed > 0f)
        {
            dispersionElapsed -= Time.deltaTime;
            if (dispersionElapsed <= 0f)
            {
                trayRoot.gameObject.SetActive(false);
                StopArmFollowing();
            }
        }
    }

    /// <summary>Called by the Main scene's accepted feeding voice command.</summary>
    public void EnterFeedingWait()
    {
        if (DiverStartupGate.IsBlocked(gameObject.scene)) return;
        if (IsFeedingActive) return;
        if (trayRoot == null || food == null || interaction == null)
            return;
        CancelFeedingAlignment();
        CancelFishFeeding();
        StopArmFollowing();
        trayRoot.gameObject.SetActive(false);
        foreach (var diver in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
        {
            if (diver.gameObject.scene != gameObject.scene) continue;
            var movement = diver.GetComponent<DiverMovementController>();
            if (movement) movement.ResetMovement();
            feedingView = diver;
            feedingView.SetFeedingControlLock(true);
            if (!diver.TryAlignBodyToView(out int version)) { StopArmFollowing(); return; }
            feedingAlignment = StartCoroutine(WaitForFeedingAlignment(diver,version));
            return;
        }
        Debug.LogWarning("[MainFeeding] Feeding cancelled because no diver view controller is available.", this);
    }

    private IEnumerator WaitForFeedingAlignment(DiverViewController diver,int version)
    {
        // Always wait at least one frame: the pose is committed in the view controller's LateUpdate.
        yield return null;
        while (diver && diver.isActiveAndEnabled && diver.AlignmentVersion == version && diver.IsAligningToView)
            yield return null;
        feedingAlignment = null;
        if (!isActiveAndEnabled || !diver || !diver.isActiveAndEnabled || diver.AlignmentVersion != version ||
            DiverStartupGate.IsBlocked(gameObject.scene)) { StopArmFollowing(); yield break; }
        if (diver.ViewCamera) viewCamera = diver.ViewCamera;
        if (pose) pose.SetTargetCamera(viewCamera);
        BeginFeedingPresentation();
    }

    private void CancelFeedingAlignment()
    {
        if (feedingAlignment != null) StopCoroutine(feedingAlignment);
        feedingAlignment = null;
    }

    private void BeginFeedingPresentation()
    {
        CancelFishFeeding();
        loggedNoFeedingFish = false;
        nextFishSearchTime = 0f;
        // Place the tray in the horizontal view frame immediately, even while pitch is smoothing back.
        Vector3 flatForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        Quaternion horizontalView = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        trayRoot.position = viewCamera.transform.position + horizontalView * trayCameraOffset;
        trayRoot.rotation = Quaternion.identity;
        trayRoot.gameObject.SetActive(true);
        food.transform.SetParent(trayRoot,true);
        food.transform.localPosition = new Vector3(0f, 0.055f, 0f);
        food.SetActive(true);
        interaction.Configure(handTarget,food.transform,trayRoot,viewCamera);
        currentFoodColorIndex = foodPalette != null && foodPalette.Length > 0 ? Random.Range(0, foodPalette.Length) : 0;
        currentFoodColor = foodPalette != null && foodPalette.Length > 0
            ? foodPalette[currentFoodColorIndex] : new Color(1f, 0.45f, 0.12f);
        ApplyFoodColor(currentFoodColor);
        dispersion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        interaction.ResetInteraction();
        dispersed = false;
        dispersionElapsed = 0f;
        keyboardHandLocal = trayCameraOffset;
        BeginArmFollowing();
        StartFishGathering();
    }

    private bool ConnectArmRig()
    {
        if (armRig != null && armRig.IsReady) return true;
        foreach (DiverViewController diver in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
        {
            if (diver.gameObject.scene != gameObject.scene) continue;
            armRig = diver.GetComponent<KinectArmRigFollower>();
            if (armRig == null) armRig = diver.gameObject.AddComponent<KinectArmRigFollower>();
            if (!armRig.IsReady)
            {
                Debug.LogWarning("[MainFeeding] Right arm bones were not found under the diver: upper_arm.R / forearm.R / hand.R.", diver);
                return false;
            }
            armRig.SetTargetCamera(viewCamera != null ? viewCamera.transform : null);
            Debug.Log("[MainFeeding] Connected diver right arm with the confirmed bone rotation corrections.", armRig);
            return true;
        }
        Debug.LogWarning("[MainFeeding] No DiverViewController found in this scene. Food input is available, but the model arm cannot follow.", this);
        return false;
    }

    private void BeginArmFollowing()
    {
        if (pauseDiverAnimationWhileFeeding)
            foreach (DiverViewController diver in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
            {
                if (diver.gameObject.scene != gameObject.scene) continue;
                animationPause.Pause(diver.GetComponentsInChildren<Animator>(true));
                break;
            }
        if (!ConnectArmRig()) return;
        // Repeated feeding commands must retain the original pose and animator state.
        if (!armFollowing) armRig.SaveLocalPose();
        armRig.SetInitialShoulderAlignment(false);
        armFollowing = true;
        hasArmPose = false;
    }

    private void StopArmFollowing()
    {
        if (feedingView) feedingView.SetFeedingControlLock(false);
        feedingView = null;
        if (armFollowing && armRig != null)
        {
            armRig.RestoreLocalPose();
            armRig.SetInitialShoulderAlignment(true);
        }
        armFollowing = false;
        hasArmPose = false;
        animationPause.Restore();
    }

    public void CancelFeeding()
    {
        CancelFeedingAlignment();
        CancelFishFeeding();
        StopArmFollowing();
        if (interaction != null) interaction.ResetInteraction();
        if (trayRoot != null) trayRoot.gameObject.SetActive(false);
        if (dispersion != null) dispersion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dispersed = false;
    }

    private void OnDisable() => CancelFeeding();

    private void StartFishGathering()
    {
        if (feedingGroup != null || Time.unscaledTime < nextFishSearchTime) return;
        nextFishSearchTime = Time.unscaledTime + 1f;
        feedingGroup = SummonFish.GatherForFeeding(viewCamera.transform, food.transform, currentFoodColorIndex,
            currentFoodColor, gatheringFishCount, gatheringDistance, gatheringRadius, gatheringSearchRadius);
        if (feedingGroup == null && !loggedNoFeedingFish)
        {
            loggedNoFeedingFish = true;
            Debug.LogWarning("[MainFeeding] No nearby fish has a valid material color set for this food. Check fish color settings and gathering search radius.", this);
        }
    }

    private void UpdateFishFeeding()
    {
        if (feedingGroup != null && feedingGroup.HasWinner) { DisperseFood(); return; }
        if (dispersed || !trayRoot.gameObject.activeSelf) { CancelFishFeeding(); return; }
        if (feedingGroup != null && feedingGroup.Count == 0) CancelFishFeeding();
        StartFishGathering();
        // Begin the race as soon as the lifted food is actually extended, without the old 5-second hold.
        if (feedingGroup != null) feedingGroup.SetOffering(interaction.IsOffering);
    }

    private void CancelFishFeeding()
    {
        feedingGroup?.Cancel();
        feedingGroup = null;
    }

    private void UpdateKeyboardTest(float deltaTime)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.fKey.wasPressedThisFrame && !trayRoot.gameObject.activeSelf && feedingAlignment == null)
        {
            DiverAudioController.PlayRecognition(gameObject.scene);
            EnterFeedingWait();
        }
        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            CancelFeedingAlignment();
            CancelFishFeeding();
            interaction.ResetInteraction();
            trayRoot.gameObject.SetActive(false);
            dispersed = false;
            dispersionElapsed = 0f;
            StopArmFollowing();
        }
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float y = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        Vector3 input = new Vector3(x, y, z);
        if (input.sqrMagnitude > 1f) input.Normalize();
        float speed = keyboardHandSpeed * (keyboard.leftShiftKey.isPressed ? 2.5f : 1f);
        keyboardHandLocal += input * speed * Mathf.Max(0f, deltaTime);
    }

    private void DisperseFood()
    {
        if(dispersed)return;
        DiverAudioController.PlayFeeding(gameObject.scene);
        CancelFishFeeding();
        dispersed = true;
        food.SetActive(false);
        dispersion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dispersion.main;
        main.startColor = currentFoodColor;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.8f, 0.65f), new GradientAlphaKey(0f, 1f) });
        var colorOverLifetime = dispersion.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);
        Vector3 origin = food.transform.position;
        for (int i = 0; i < Mathf.Clamp(dispersionParticleCount, 8, 128); i++)
        {
            Vector3 velocity = Random.insideUnitSphere * Random.Range(0.10f, 0.34f);
            velocity.y *= 0.55f;
            var particle = new ParticleSystem.EmitParams
            {
                position = origin,
                velocity = velocity,
                startColor = currentFoodColor,
                startSize = Random.Range(0.012f, 0.034f),
                startLifetime = Random.Range(1.1f, 2f)
            };
            dispersion.Emit(particle, 1);
        }
        dispersion.Play(true);
        dispersionElapsed = 2.1f;
        interaction.ConsumeFood();
        if (interaction.State != ScoopFeedingState.AwaitEntry) interaction.ResetInteraction();
    }

    private void ApplyFoodColor(Color color)
    {
        foodRenderer.GetPropertyBlock(foodProperties);
        foodProperties.SetColor("_BaseColor", color);
        foodProperties.SetColor("_Color", color);
        foodRenderer.SetPropertyBlock(foodProperties);
    }

    private void OnGUI()
    {
        if (DiverStartupGate.IsBlocked(gameObject.scene) || HudOwnsGuide || !keyboardTestMode || interaction == null) return;
        GUILayout.BeginArea(new Rect(16f, Mathf.Max(16f, Screen.height - 155f), Mathf.Min(720f, Screen.width - 32f), 140f), GUI.skin.box);
        GUILayout.Label("FEEDING TEST | Voice / F: show tray | Cyan dot: hand position");
        GUILayout.Label("W/S: forward/back | A/D: left/right | E/Q: up/down");
        GUILayout.Label("Shift: faster | Backspace: reset | Click Game View for key input");
        if (trayRoot != null && trayRoot.gameObject.activeSelf)
            GUILayout.Label(FeedingGuide);
        if (!dispersed && interaction.IsOffering)
            GUILayout.Label(feedingGroup != null ? "Nearby fish are racing for the food..." : "Waiting for a StripedFish with configured material color sets...");
        GUILayout.EndArea();
    }

    private static Material MakeMaterial(string shaderName, Color color)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        return material;
    }

    private void OnDestroy()
    {
        CancelFeedingAlignment();
        CancelFishFeeding();
        StopArmFollowing();
        if (keyboardHandMarkerMaterial != null) Destroy(keyboardHandMarkerMaterial);
        if (handTarget != null) Destroy(handTarget.gameObject);
        if (trayRoot != null)
        {
            foreach (Renderer renderer in trayRoot.GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null) Destroy(renderer.sharedMaterial);
            Destroy(trayRoot.gameObject);
        }
    }
}
