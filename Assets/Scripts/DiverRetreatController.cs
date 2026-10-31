using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Records only the active session; shutdown and map review never reload the scene or model.</summary>
[DefaultExecutionOrder(300)]
public sealed class DiverRetreatController : MonoBehaviour
{
    [SerializeField, Min(.5f)] float shutdownSeconds=2.4f;
    [SerializeField, Min(1f)] float replaySeconds=8f;
    [SerializeField, InspectorName("軌跡の範囲（省略時はSeabedを検索）")] Transform seaBed;
    readonly DiverPathRecording path=new DiverPathRecording();
    public double TravelDistance => path.Distance;
    DiverViewController diver;
    DiverHudController hud;
    DiverFeedingFoodController feeding;
    Canvas canvas;
    Image shade;
    Text status;
    Font font;
    bool ownsFont, started, retreating, reviewing;
    float startedAt, nextSample, replayTime, nextFind;
    DiverPathGraphic line;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded-=OnLoaded; SceneManager.sceneLoaded+=OnLoaded;
        for(int i=0;i<SceneManager.sceneCount;i++)Ensure(SceneManager.GetSceneAt(i));
    }
    static void OnLoaded(Scene scene,LoadSceneMode mode)=>Ensure(scene);
    static DiverRetreatController Ensure(Scene scene)
    {
        if(scene.name!="Main"||!scene.isLoaded)return null;
        foreach(var c in FindObjectsByType<DiverRetreatController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(c.gameObject.scene==scene)return c;
        var root=new GameObject("DiverRetreat"); SceneManager.MoveGameObjectToScene(root,scene);
        return root.AddComponent<DiverRetreatController>();
    }
    public static void Request(Scene scene)
    {
        var controller=Ensure(scene);
        if(controller)controller.BeginRetreat();
    }
    T FindSource<T>() where T:Component
    {
        foreach(var c in FindObjectsByType<T>(FindObjectsSortMode.None))if(c.gameObject.scene==gameObject.scene)return c;
        return null;
    }
    void Update()
    {
        if(Time.unscaledTime>=nextFind && !retreating)
        {
            nextFind=Time.unscaledTime+1;
            diver=FindSource<DiverViewController>();hud=FindSource<DiverHudController>();feeding=FindSource<DiverFeedingFoodController>();
        }
        if(reviewing)
        {
            if(Keyboard.current!=null && Keyboard.current.spaceKey.wasPressedThisFrame)replayTime=0;
            replayTime+=Time.unscaledDeltaTime;
            float p=Mathf.Clamp01(replayTime/Mathf.Max(1,replaySeconds));line.SetProgress(p);
            status.text=p<1?"ROUTE PLAYBACK  /  "+Mathf.RoundToInt(p*100)+"%":"ROUTE COMPLETE  /  SPACE：もう一度再生";
            return;
        }
        if(retreating || DiverStartupGate.IsBlocked(gameObject.scene) || !diver || !diver.BodyRoot)return;
        if(!started) { started=true; startedAt=Time.unscaledTime; }
        if(Time.unscaledTime>=nextSample) { Sample(); nextSample=Time.unscaledTime+.2f; }
    }
    void Sample(bool final=false)
    {
        if(!diver || !diver.BodyRoot)return;
        var p=diver.BodyRoot.position;path.Sample(p.x,p.y,p.z,final);
    }
    public void BeginRetreat()
    {
        if(retreating || DiverStartupGate.IsBlocked(gameObject.scene))return;
        if(!diver)diver=FindSource<DiverViewController>();
        if(!diver || !diver.BodyRoot) { Debug.LogWarning("[Retreat] No diver body found.",this); return; }
        var position=diver.BodyRoot.position;
        if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))return;
        if(!started) {started=true;startedAt=Time.unscaledTime;}
        Sample(true);
        retreating=true;
        DiverStartupGate.Begin(gameObject.scene);
        var movement=FindSource<DiverMovementController>();if(movement)movement.ResetMovement();
        feeding=FindSource<DiverFeedingFoodController>();if(feeding)feeding.CancelFeeding();
        // Disabling uses the existing cancellation/disposal owner, never creates another model.
        foreach(var voice in FindObjectsByType<VoskOfflineVoiceCommands>(FindObjectsSortMode.None))
            if(voice.gameObject.scene==gameObject.scene)voice.enabled=false;
        hud=FindSource<DiverHudController>();
        BuildOverlay();
        StartCoroutine(ShutdownAndReview(Time.unscaledTime-startedAt));
        Debug.Log("[Retreat] Shutdown started. Path samples="+path.Points.Count,this);
    }
    void BuildOverlay()
    {
        var root=new GameObject("RetreatDisplay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(root,gameObject.scene);
        canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=300;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        foreach(string candidate in new[]{"Yu Gothic UI","Meiryo"})
            if(System.Array.Exists(Font.GetOSInstalledFontNames(),v=>v==candidate)) {font=Font.CreateDynamicFontFromOSFont(candidate,28);ownsFont=true;break;}
        if(!font)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        shade=FullRect("Blackout",root.transform).gameObject.AddComponent<Image>();shade.color=Color.clear;shade.raycastTarget=false;
        status=Label("ROBOT SHUTDOWN",new Vector2(.08f,.43f),new Vector2(.92f,.57f),32);
    }
    IEnumerator ShutdownAndReview(float duration)
    {
        float elapsed=0;
        while(elapsed<Mathf.Max(.5f,shutdownSeconds))
        {
            elapsed+=Time.unscaledDeltaTime;float p=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/Mathf.Max(.5f,shutdownSeconds)));
            if(hud)hud.SetShutdownProgress(p);
            shade.color=new Color(0,.008f,.012f,p);
            status.text=p<.55f?"体験を終了します\nROBOT SHUTDOWN":"視覚システムを終了しています\nPREPARING ROUTE";
            yield return null;
        }
        // A schematic only: no map camera, RenderTexture, or global fog changes.
        var bounds=SeaBedBounds();
        float width=Mathf.Max(.01f,bounds.size.x), height=Mathf.Max(.01f,bounds.size.z);
        shade.color=new Color(.012f,.025f,.04f,1);
        RectTransform area=FullRect("RouteArea",canvas.transform);area.anchorMin=new Vector2(.08f,.20f);area.anchorMax=new Vector2(.92f,.82f);
        RectTransform panel=FullRect("SeaBedBoundary",area);
        var fit=panel.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.FitInParent;fit.aspectRatio=width/height;
        var background=panel.gameObject.AddComponent<Image>();background.color=Color.white;background.raycastTarget=false;
        panel.gameObject.AddComponent<RectMask2D>();
        float gridStep = DiverRouteGrid.Step(width,height);
        BuildGrid(panel,width,height,gridStep);
        line=FullRect("RecordedRoute",panel).gameObject.AddComponent<DiverPathGraphic>();line.color=new Color(.15f,.67f,.9f);line.raycastTarget=false;
        var projected=new Vector2[path.Points.Count];
        for(int i=0;i<projected.Length;i++) {var p=path.Points[i];projected[i]=new Vector2(.5f+(p.X-bounds.center.x)/width,.5f+(p.Z-bounds.center.z)/height);}
        line.SetPath(projected);line.SetProgress(0);
        DrawBorder(panel, new Vector2(0,0),new Vector2(1,0),new Vector2(0,2));
        DrawBorder(panel, new Vector2(0,1),new Vector2(1,1),new Vector2(0,2));
        DrawBorder(panel, new Vector2(0,0),new Vector2(0,1),new Vector2(2,0));
        DrawBorder(panel, new Vector2(1,0),new Vector2(1,1),new Vector2(2,0));
        status.rectTransform.anchorMin=new Vector2(.08f,.04f);status.rectTransform.anchorMax=new Vector2(.92f,.13f);status.fontSize=24;
        status.transform.SetAsLastSibling();
        Label("航行記録  /  SESSION ROUTE",new Vector2(.08f,.87f),new Vector2(.92f,.96f),34);
        Label($"N ↑  ／  枠：SeaBed全体  {width:0.#} × {height:0.#} m  ／  1マス {gridStep:0.##} m\n移動距離 {path.Distance:F1} m    体験時間 {duration:F0} 秒",new Vector2(.08f,.105f),new Vector2(.92f,.19f),22);
        status.color=new Color(.5f,1,.95f);
        reviewing=true;replayTime=0;
        Debug.Log("[Retreat] Route review with distance grid ready. World bounds="+bounds,this);
    }
    void BuildGrid(RectTransform panel,float width,float height,float step)
    {
        for(int i=0;i*step<width && i<=12;i++)
        {
            float metres=i*step, x=metres/width;
            GridLine(panel,new Vector2(x,0),new Vector2(x,1),new Vector2(1,0));
            if(i==0 || width-metres>=step*.5f)
                GridLabel(panel,metres,new Vector2(x,0),new Vector2(x<.05f?0:.5f,0),new Vector2(4,3));
        }
        GridLabel(panel,width,new Vector2(1,0),new Vector2(1,0),new Vector2(-4,3));
        for(int i=1;i*step<height && i<=12;i++)
        {
            float metres=i*step, y=metres/height;
            GridLine(panel,new Vector2(0,y),new Vector2(1,y),new Vector2(0,1));
            if(height-metres>=step*.5f)
                GridLabel(panel,metres,new Vector2(0,y),new Vector2(0,.5f),new Vector2(4,0));
        }
        GridLabel(panel,height,new Vector2(0,1),new Vector2(0,1),new Vector2(4,-3));
    }
    static void GridLine(RectTransform panel,Vector2 min,Vector2 max,Vector2 size)
    {
        var r=FullRect("DistanceGrid",panel);r.anchorMin=min;r.anchorMax=max;r.sizeDelta=size;
        var image=r.gameObject.AddComponent<Image>();image.color=new Color(.82f,.86f,.89f);image.raycastTarget=false;
    }
    void GridLabel(RectTransform panel,float metres,Vector2 anchor,Vector2 pivot,Vector2 offset)
    {
        var r=FullRect("DistanceMark",panel);r.anchorMin=r.anchorMax=anchor;r.pivot=pivot;r.sizeDelta=new Vector2(85,22);r.anchoredPosition=offset;
        var text=r.gameObject.AddComponent<Text>();text.font=font;text.fontSize=13;text.text=metres.ToString("0.##")+" m";
        text.alignment=pivot.x==1?TextAnchor.MiddleRight:pivot.x==0?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter;text.color=new Color(.3f,.4f,.46f);text.raycastTarget=false;
    }
    Bounds SeaBedBounds()
    {
        if(SeaBedWorldBounds.TryGet(gameObject.scene,seaBed,out Bounds bounds))return bounds;
        Debug.LogWarning("[Retreat] Seabed bounds unavailable. Assign the SeaBed transform on DiverRetreat. Using route bounds as fallback.",this);
        bounds=PathBounds();bounds.Expand(new Vector3(5,0,5));return bounds;
    }
    static void DrawBorder(RectTransform parent,Vector2 min,Vector2 max,Vector2 size)
    {
        var r=FullRect("BoundaryLine",parent);r.anchorMin=min;r.anchorMax=max;
        r.pivot=new Vector2(min.x==max.x?min.x:.5f,min.y==max.y?min.y:.5f);r.sizeDelta=size;
        var image=r.gameObject.AddComponent<Image>();image.color=new Color(.3f,.65f,.8f);image.raycastTarget=false;
    }
    Bounds PathBounds()
    {
        var first=path.Points[0];var b=new Bounds(new Vector3(first.X,first.Y,first.Z),Vector3.zero);
        foreach(var p in path.Points)b.Encapsulate(new Vector3(p.X,p.Y,p.Z));return b;
    }
    static RectTransform FullRect(string name,Transform parent)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);
        r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;
    }
    Text Label(string text,Vector2 min,Vector2 max,int size)
    {
        var r=FullRect("Readout",canvas.transform);r.anchorMin=min;r.anchorMax=max;
        var label=r.gameObject.AddComponent<Text>();label.font=font;label.text=text;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;label.color=new Color(.5f,1,.95f);label.raycastTarget=false;return label;
    }
    void OnDisable(){StopAllCoroutines();if(canvas)canvas.gameObject.SetActive(false);}
    void OnDestroy()
    {
        if(canvas)Destroy(canvas.gameObject);if(ownsFont&&font)Destroy(font);
    }
}
