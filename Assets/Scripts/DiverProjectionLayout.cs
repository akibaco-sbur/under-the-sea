using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Shared UI calibration; it does not change the camera or the projection geometry.</summary>
[DisallowMultipleComponent]
public sealed class DiverProjectionLayout : MonoBehaviour
{
    static readonly Dictionary<int,DiverProjectionLayout> layouts=new Dictionary<int,DiverProjectionLayout>();
    [SerializeField, InspectorName("投影範囲にUIを収める")] bool useProjectionArea=true;
    [SerializeField, InspectorName("UI表示範囲（左下原点・0～1）")] Rect displayArea=new Rect(.22f,.14f,.43f,.7f);
    [SerializeField, Range(.25f,1.5f), InspectorName("UI全体倍率")] float uiScale=1f;
    [SerializeField, InspectorName("調整枠を表示")] bool showCalibrationFrame;
    [SerializeField, InspectorName("保存済み設定を起動時に読む")] bool loadSavedProfile=true;
    [Serializable] sealed class Profile {public Rect DisplayArea;public float UiScale=1;public bool UseProjectionArea=true;}
    string ProfilePath=>Path.Combine(Application.persistentDataPath,"ProjectionLayout.json");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset()=>layouts.Clear();
    public static DiverProjectionLayout ForScene(Scene scene)
    {
        if(!scene.isLoaded || scene.name!="Main")return null;
        if(layouts.TryGetValue(scene.handle,out var owner)&&owner)return owner;
        foreach(var existing in FindObjectsByType<DiverProjectionLayout>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(existing.gameObject.scene==scene){layouts[scene.handle]=existing;return existing;}
        var root=new GameObject("ProjectionUiSettings");SceneManager.MoveGameObjectToScene(root,scene);
        var created=root.AddComponent<DiverProjectionLayout>();layouts[scene.handle]=created;return created;
    }
    void Awake(){if(loadSavedProfile)LoadProfile();}
    public Rect NormalizedArea
    {
        get
        {
            if(!useProjectionArea)return new Rect(0,0,1,1);
            float x=Mathf.Clamp(displayArea.x,0,.9f),y=Mathf.Clamp(displayArea.y,0,.9f);
            return new Rect(x,y,Mathf.Clamp(displayArea.width,.1f,1-x),Mathf.Clamp(displayArea.height,.1f,1-y));
        }
    }
    public static void Apply(RectTransform content,Canvas canvas)
    {
        var owner=ForScene(canvas.gameObject.scene);if(!owner || !content)return;
        Rect area=owner.NormalizedArea;Rect parent=((RectTransform)canvas.transform).rect;
        content.anchorMin=content.anchorMax=area.center;content.pivot=new Vector2(.5f,.5f);content.anchoredPosition=Vector2.zero;
        content.sizeDelta=new Vector2(1920,1080);
        content.localScale=Vector3.one*DiverProjectionMath.Fit(parent.width,parent.height,area.width,area.height,owner.uiScale);
    }
    public Rect PixelArea=>new Rect(NormalizedArea.x*Screen.width,(1-NormalizedArea.yMax)*Screen.height,NormalizedArea.width*Screen.width,NormalizedArea.height*Screen.height);
    [ContextMenu("投影UI設定を保存")]
    public void SaveProfile()
    {
        try {Directory.CreateDirectory(Application.persistentDataPath);File.WriteAllText(ProfilePath,JsonUtility.ToJson(new Profile{DisplayArea=NormalizedArea,UiScale=uiScale,UseProjectionArea=useProjectionArea},true));Debug.Log("[ProjectionUI] Saved: "+ProfilePath,this);}
        catch(Exception e){Debug.LogError("[ProjectionUI] Save failed: "+e.Message,this);}
    }
    [ContextMenu("投影UI設定を読込")]
    public void LoadProfile()
    {
        try {if(!File.Exists(ProfilePath))return;var p=JsonUtility.FromJson<Profile>(File.ReadAllText(ProfilePath));if(p!=null){displayArea=p.DisplayArea;uiScale=p.UiScale;useProjectionArea=p.UseProjectionArea;}}
        catch(Exception e){Debug.LogWarning("[ProjectionUI] Profile unavailable: "+e.Message,this);}
    }
    void OnGUI()
    {
        if(!showCalibrationFrame)return;Rect r=PixelArea;Color previous=GUI.color;GUI.color=Color.cyan;
        GUI.DrawTexture(new Rect(r.x,r.y,r.width,2),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.x,r.yMax-2,r.width,2),Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x,r.y,2,r.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.xMax-2,r.y,2,r.height),Texture2D.whiteTexture);
        GUI.Label(new Rect(r.x+8,r.y+5,r.width-16,24),"PROJECTION UI AREA / Inspector: ProjectionUiSettings");GUI.color=previous;
    }
    void OnDestroy(){if(layouts.TryGetValue(gameObject.scene.handle,out var owner)&&owner==this)layouts.Remove(gameObject.scene.handle);}
}
