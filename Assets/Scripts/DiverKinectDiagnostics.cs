using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Observes the existing sensor only. It never starts, resets or substitutes Kinect input.</summary>
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public sealed class DiverKinectDiagnostics : MonoBehaviour
{
    [SerializeField, InspectorName("診断画面を表示")] bool showDiagnostics;
    [SerializeField, InspectorName("診断キーF7/F9/F10を有効")] bool enableDiagnosticKeys=true;
    AzureKinectSkeletonView skeleton;
    KinectScoopPose pose;
    VoskOfflineVoiceCommands voice;
    DiverFeedingFoodController feeding;
    KinectStartupController startup;
    DiverViewController view;
    DiverProjectionLayout projection;
    Canvas canvas;
    RectTransform content;
    Text readout;
    BoundedDiagnosticCsv csv;
    string folder,filePath,phase="1_Seated",lastResult="Not recording";
    float nextFind,nextSample;
    double startedAt;
    int recordings,rows,upperValidRows,armValidRows,palmUpRows;
    double maxAge;
    static readonly int[] JointIds={2,5,12,13,14,15,16,17,26};
    static readonly string[] JointNames={"chest","left_shoulder","right_shoulder","right_elbow","right_wrist","right_hand","right_handtip","right_thumb","head"};
    [Serializable] sealed class Metadata
    {
        public string RecordedAt,OperatingSystem,Cpu,Gpu,UnityVersion,Screen,Projection,Startup,Kinect,Arm,Voice;
        public int RamMb,Rows,UpperBodyValidRows,ArmTrackedRows,PalmUpRows;
        public double MaximumFrameAgeSeconds;
        public bool UsedDiagnosticBypass;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded-=OnLoaded;SceneManager.sceneLoaded+=OnLoaded;
        for(int i=0;i<SceneManager.sceneCount;i++)Ensure(SceneManager.GetSceneAt(i));
    }
    static void OnLoaded(Scene scene,LoadSceneMode mode)=>Ensure(scene);
    static void Ensure(Scene scene)
    {
        if(!scene.isLoaded||scene.name!="Main")return;
        foreach(var existing in FindObjectsByType<DiverKinectDiagnostics>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(existing.gameObject.scene==scene)return;
        var root=new GameObject("KinectDiagnostics");SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<DiverKinectDiagnostics>();
    }
    public static bool IsRecordingScene(Scene scene)
    {
        foreach(var test in FindObjectsByType<DiverKinectDiagnostics>(FindObjectsSortMode.None))if(test.gameObject.scene==scene&&test.csv!=null)return true;
        return false;
    }
    T Find<T>() where T:Component
    {foreach(var c in FindObjectsByType<T>(FindObjectsSortMode.None))if(c.gameObject.scene==gameObject.scene)return c;return null;}
    void FindSources(){skeleton=Find<AzureKinectSkeletonView>();pose=Find<KinectScoopPose>();voice=Find<VoskOfflineVoiceCommands>();feeding=Find<DiverFeedingFoodController>();startup=Find<KinectStartupController>();view=Find<DiverViewController>();}
    void Awake()
    {
        folder=Path.Combine(Application.persistentDataPath,"KinectDiagnostics");
        var root=new GameObject("KinectDiagnosticDisplay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));root.transform.SetParent(transform,false);
        canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2500;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        content=new GameObject("ProjectionContent",typeof(RectTransform)).GetComponent<RectTransform>();content.SetParent(root.transform,false);
        var panel=new GameObject("DiagnosticPanel",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();panel.SetParent(content,false);
        panel.anchorMin=new Vector2(.025f,.12f);panel.anchorMax=new Vector2(.81f,.95f);panel.offsetMin=panel.offsetMax=Vector2.zero;
        panel.GetComponent<Image>().color=new Color(.01f,.025f,.035f,.96f);panel.GetComponent<Image>().raycastTarget=false;
        readout=new GameObject("Readout",typeof(RectTransform),typeof(Text)).GetComponent<Text>();readout.transform.SetParent(panel,false);
        var r=readout.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(24,18);r.offsetMax=new Vector2(-24,-18);
        readout.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");readout.fontSize=26;readout.color=new Color(.65f,1,.95f);readout.raycastTarget=false;
        canvas.gameObject.SetActive(showDiagnostics);
    }
    void Update()
    {
        var key=Keyboard.current;
        if(enableDiagnosticKeys && key!=null)
        {
            if(key.f7Key.wasPressedThisFrame)showDiagnostics=!showDiagnostics;
            if(key.f8Key.wasPressedThisFrame && csv!=null && startup)startup.BeginDiagnosticExperience();
            if(key.f9Key.wasPressedThisFrame)StartRecording();
            if(key.f10Key.wasPressedThisFrame)StopRecording();
            if(csv!=null)
            {
                if(key.digit1Key.wasPressedThisFrame)phase="1_Seated";if(key.digit2Key.wasPressedThisFrame)phase="2_PalmUp";
                if(key.digit3Key.wasPressedThisFrame)phase="3_PalmDown";if(key.digit4Key.wasPressedThisFrame)phase="4_Scoop";
                if(key.digit5Key.wasPressedThisFrame)phase="5_Offer";if(key.digit6Key.wasPressedThisFrame)phase="6_Stand";
            }
        }
        if(canvas.gameObject.activeSelf!=showDiagnostics)canvas.gameObject.SetActive(showDiagnostics);
        if(showDiagnostics)DiverProjectionLayout.Apply(content,canvas);
        if(!showDiagnostics && csv==null)return;
        if(Time.unscaledTime>=nextFind){nextFind=Time.unscaledTime+1;FindSources();}
        if(Time.unscaledTime<nextSample)return;nextSample=Time.unscaledTime+.1f;
        Sample();
        if(csv!=null && Time.realtimeSinceStartupAsDouble-startedAt>=600)StopRecording();
    }
    [ContextMenu("診断記録を開始（F9）")]
    public void StartRecording()
    {
        if(csv!=null)return;
        if(recordings>=4){Debug.LogWarning("[KinectTest] Four test recordings reached. Restart Play to record more.",this);return;}
        try
        {
            FindSources();Directory.CreateDirectory(folder);
            projection=DiverProjectionLayout.ForScene(gameObject.scene);
            filePath=Path.Combine(folder,"KinectTest_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".csv");
            var header=new StringBuilder("windows_time,phase,frame,age_s,body_count,selected_id,diagnostic_body_id,upper_valid,start_eligible,start_progress,game_started,arm_tracked,estimated,hand_orientation,palm_up,palm_dot,palm_threshold,axis_x_up,axis_y_up,axis_z_up,feeding_active,feeding_state,touching,hand_food_distance_m,hand_world_x,hand_world_y,hand_world_z,tray_world_x,tray_world_y,tray_world_z");
            header.Append(",camera_euler_x,camera_euler_y,camera_euler_z");
            foreach(var name in JointNames)header.Append(","+name+"_confidence,"+name+"_x_mm,"+name+"_y_mm,"+name+"_z_mm,"+name+"_qx,"+name+"_qy,"+name+"_qz,"+name+"_qw");
            header.Append(",sensor_status,arm_status,candidate_chests_mm");
            csv=new BoundedDiagnosticCsv(filePath,header.ToString());recordings++;rows=upperValidRows=armValidRows=palmUpRows=0;maxAge=0;phase="1_Seated";
            startedAt=Time.realtimeSinceStartupAsDouble;showDiagnostics=true;lastResult="Recording: "+Path.GetFileName(filePath);SaveMetadata();
            Debug.Log("[KinectTest] Recording: "+filePath+" (10 Hz; 10 min / 8 MiB limit).",this);
        }
        catch(Exception e){if(csv!=null){csv.Dispose();csv=null;}Debug.LogError("[KinectTest] Cannot start: "+e.Message,this);}
    }
    [ContextMenu("診断記録を終了（F10）")]
    public void StopRecording()
    {
        if(csv==null)return;
        var closing=csv;csv=null;
        try{closing.Dispose();SaveMetadata();lastResult="Saved "+rows+" rows: "+Path.GetFileName(filePath);Debug.Log("[KinectTest] Saved CSV + JSON: "+filePath,this);}
        catch(Exception e){Debug.LogError("[KinectTest] Stop failed: "+e.Message,this);}
    }
    void SaveMetadata()
    {
        var layout=projection;
        var data=new Metadata{RecordedAt=DateTimeOffset.Now.ToString("O"),OperatingSystem=SystemInfo.operatingSystem,Cpu=SystemInfo.processorType,Gpu=SystemInfo.graphicsDeviceName,RamMb=SystemInfo.systemMemorySize,UnityVersion=Application.unityVersion,Screen=Screen.width+"x"+Screen.height,
            Projection=layout?JsonUtility.ToJson(layout):"",Startup=startup?JsonUtility.ToJson(startup):"",Kinect=skeleton?JsonUtility.ToJson(skeleton):"",Arm=pose?JsonUtility.ToJson(pose):"",Voice=voice?JsonUtility.ToJson(voice):"",Rows=rows,UpperBodyValidRows=upperValidRows,ArmTrackedRows=armValidRows,PalmUpRows=palmUpRows,MaximumFrameAgeSeconds=maxAge,UsedDiagnosticBypass=startup&&startup.IsDiagnosticStart};
        File.WriteAllText(Path.ChangeExtension(filePath,".json"),JsonUtility.ToJson(data,true));
    }
    static string N(double value)=>double.IsNaN(value)||double.IsInfinity(value)?"":value.ToString("0.#####",CultureInfo.InvariantCulture);
    static int B(bool value)=>value?1:0;
    static AzureKinectNative.Joint Joint(AzureKinectCapture.Body body,int id)=>body!=null&&body.Joints!=null&&id<body.Joints.Length?body.Joints[id]:default;
    void Sample()
    {
        var snapshot=skeleton?skeleton.DiagnosticSnapshot:null;
        double age=snapshot==null?double.PositiveInfinity:(System.Diagnostics.Stopwatch.GetTimestamp()-snapshot.ReceivedTicks)/(double)System.Diagnostics.Stopwatch.Frequency;
        int count=snapshot!=null&&snapshot.Bodies!=null?snapshot.Bodies.Length:0;
        uint selected=startup&&startup.HasExperienceStarted&&startup.RecognizedBodyId!=0?startup.RecognizedBodyId:pose?pose.BodyId:0;
        AzureKinectCapture.Body body=null;
        if(snapshot!=null&&snapshot.Bodies!=null)
            foreach(var candidate in snapshot.Bodies)if(candidate!=null&&(candidate.Id==selected || (selected==0&&count==1))){body=candidate;break;}
        if(selected==0 && body==null && snapshot!=null && snapshot.Bodies!=null)
            foreach(var candidate in snapshot.Bodies)
                if(candidate!=null && Joint(candidate,2).Z>0 && (body==null || Joint(candidate,2).Z<Joint(body,2).Z))body=candidate;
        bool upper=body!=null&&age>=0&&age<=.35&&KinectStartupBodyValidation.IsUpperBodyValid(body.Joints);
        bool tracked=pose&&pose.IsTracked, palm=voice&&voice.IsPalmFacingUp;
        Vector3 hand=feeding?feeding.DiagnosticPalmPosition:Vector3.zero,tray=feeding?feeding.DiagnosticTrayPosition:Vector3.zero;
        Quaternion rotation=pose?pose.HandRotation:Quaternion.identity;
        bool orientation=tracked&&pose.HasHandOrientation;
        float x=orientation?Vector3.Dot(rotation*Vector3.right,Vector3.up):float.NaN;
        float y=orientation?Vector3.Dot(rotation*Vector3.up,Vector3.up):float.NaN;
        float z=orientation?Vector3.Dot(rotation*Vector3.forward,Vector3.up):float.NaN;
        readout.text="KINECT FIELD TEST   | F7 overlay / F9 record / F10 stop\nF8 diagnostic start (recording only; Kinect stays active)\n1 seated  2 palm-up  3 palm-down  4 scoop  5 offer  6 stand\n\n"+
            "Phase: "+phase+"  | "+lastResult+"\nSensor: "+(skeleton?skeleton.TrackingStatus:"NO EXISTING KINECT INPUT")+"\nBodies: "+count+"  Selected ID: "+selected+"  Sample ID: "+(body!=null?body.Id:0)+"  Age: "+N(age)+" s\n"+
            "Upper body: "+upper+"  Eligible: "+(upper&&count==1)+"  Progress: "+(startup?startup.RecognitionProgress:0).ToString("P0")+"  Game: "+(startup&&startup.HasExperienceStarted)+"  Bypass: "+(startup&&startup.IsDiagnosticStart)+"\n"+
            "Confidence HEAD/CHEST/L-SHOULDER/R-SHOULDER: "+Joint(body,26).Confidence+" / "+Joint(body,2).Confidence+" / "+Joint(body,5).Confidence+" / "+Joint(body,12).Confidence+"\n"+
            "Confidence ELBOW/WRIST/HAND/TIP/THUMB: "+Joint(body,13).Confidence+" / "+Joint(body,14).Confidence+" / "+Joint(body,15).Confidence+" / "+Joint(body,16).Confidence+" / "+Joint(body,17).Confidence+"\n"+
            "Arm: "+tracked+"  Estimated: "+(pose&&pose.IsEstimated)+"  Orientation: "+(pose&&pose.HasHandOrientation)+"\n"+
            "Palm up: "+palm+"  Dot: "+N(voice?voice.PalmFacingUpDot:double.NaN)+"  Threshold: "+N(voice?voice.PalmFacingUpThreshold:double.NaN)+"\n"+
            "Axis X/Y/Z up dot: "+N(x)+" / "+N(y)+" / "+N(z)+"\nFeeding: "+(feeding?feeding.DiagnosticState.ToString():"N/A")+"  Contact: "+(feeding&&feeding.DiagnosticContact)+"\n"+
            "Hand-food distance: "+N(feeding?Vector3.Distance(hand,feeding.DiagnosticFoodPosition):0)+" m\nPose: "+(pose?pose.TrackingMessage:"N/A")+"\n"+
            (feeding&&feeding.IsKeyboardTest?"WARNING: Keyboard Feeding Test Mode is ON; Kinect is bypassed.\n":"")+"\nCSV + JSON location is printed in the Console.";
        if(csv==null)return;
        var row=new StringBuilder();
        row.Append(BoundedDiagnosticCsv.Escape(DateTimeOffset.Now.ToString("O"))).Append(',').Append(phase).Append(',').Append(snapshot!=null?snapshot.FrameNumber:-1).Append(',').Append(N(age)).Append(',').Append(count).Append(',').Append(selected).Append(',').Append(body!=null?body.Id:0);
        row.Append(',').Append(B(upper)).Append(',').Append(B(upper&&count==1)).Append(',').Append(N(startup?startup.RecognitionProgress:0)).Append(',').Append(B(startup&&startup.HasExperienceStarted)).Append(',').Append(B(tracked)).Append(',').Append(B(pose&&pose.IsEstimated)).Append(',').Append(B(pose&&pose.HasHandOrientation)).Append(',').Append(B(palm));
        row.Append(',').Append(N(voice?voice.PalmFacingUpDot:double.NaN)).Append(',').Append(N(voice?voice.PalmFacingUpThreshold:double.NaN)).Append(',').Append(N(x)).Append(',').Append(N(y)).Append(',').Append(N(z));
        row.Append(',').Append(B(feeding&&feeding.IsFeedingActive)).Append(',').Append(feeding?feeding.DiagnosticState.ToString():"NA").Append(',').Append(B(feeding&&feeding.DiagnosticContact)).Append(',').Append(N(feeding?Vector3.Distance(hand,feeding.DiagnosticFoodPosition):0));
        foreach(var v in new[]{hand.x,hand.y,hand.z,tray.x,tray.y,tray.z})row.Append(',').Append(N(v));
        Vector3 angles=view&&view.ViewCamera?view.ViewCamera.transform.eulerAngles:Vector3.zero;
        foreach(var v in new[]{angles.x,angles.y,angles.z})row.Append(',').Append(N(v));
        foreach(int id in JointIds){var j=Joint(body,id);row.Append(',').Append(j.Confidence);foreach(float v in new[]{j.X,j.Y,j.Z,j.Qx,j.Qy,j.Qz,j.Qw})row.Append(',').Append(N(v));}
        row.Append(',').Append(BoundedDiagnosticCsv.Escape(skeleton?skeleton.TrackingStatus:"No source")).Append(',').Append(BoundedDiagnosticCsv.Escape(pose?pose.TrackingMessage:"No pose"));
        var candidates=new StringBuilder();
        if(snapshot!=null&&snapshot.Bodies!=null)
            foreach(var candidate in snapshot.Bodies)
            {
                if(candidate==null)continue;var chest=Joint(candidate,2);
                candidates.Append(candidate.Id).Append(':').Append(N(chest.X)).Append('/').Append(N(chest.Y)).Append('/').Append(N(chest.Z)).Append('/').Append(chest.Confidence).Append(';');
            }
        row.Append(',').Append(BoundedDiagnosticCsv.Escape(candidates.ToString()));
        try
        {
            if(!csv.Write(row.ToString())){StopRecording();return;}
            rows++;if(upper)upperValidRows++;if(tracked)armValidRows++;if(palm)palmUpRows++;if(!double.IsInfinity(age))maxAge=Math.Max(maxAge,age);
        }
        catch(Exception e){Debug.LogError("[KinectTest] Write failed: "+e.Message,this);StopRecording();}
    }
    void OnDisable(){StopRecording();if(canvas)canvas.gameObject.SetActive(false);}
    void OnDestroy(){StopRecording();}
}
