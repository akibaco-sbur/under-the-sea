using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One bounded audio owner per Main scene; no additional AudioListener.</summary>
[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class DiverAudioController : MonoBehaviour
{
    static readonly Dictionary<int,DiverAudioController> owners=new Dictionary<int,DiverAudioController>();
    [SerializeField, InspectorName("音を有効にする")] bool audioEnabled=true;
    [SerializeField, Range(0,1)] float masterVolume=.8f;
    [SerializeField, Min(.5f), InspectorName("排気音の周期（秒）")] float breathIntervalSeconds=6f;
    [Header("音源（省略時は自動取得）")]
    [SerializeField] AudioClip breathv2,feeding,inWater,noiseRoop,popup,recognision;
    [Header("音量")]
    [SerializeField, Range(0,1)] float waterVolume=.35f;
    [SerializeField, Range(0,1)] float breathVolume=.5f;
    [SerializeField, Range(0,1)] float feedingVolume=.7f;
    [SerializeField, Range(0,1)] float noiseVolume=.5f;
    [SerializeField, Range(0,1)] float popupVolume=.45f;
    [SerializeField, Range(0,1)] float recognitionVolume=.55f;
    AudioSource waterSource,breathSource,feedingSource,noiseSource,recognitionSource;
    readonly AudioSource[] popupSources=new AudioSource[2];
    bool experienceActive;
    float gain,noiseTarget,noiseGain,lastNoiseUpdate;
    double nextBreath,nextRecognition,nextPopup;
    int nextPopupSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOwners()=>owners.Clear();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded-=OnLoaded;SceneManager.sceneLoaded+=OnLoaded;
        for(int i=0;i<SceneManager.sceneCount;i++)Ensure(SceneManager.GetSceneAt(i));
    }
    static void OnLoaded(Scene scene,LoadSceneMode mode)=>Ensure(scene);
    static DiverAudioController Current(Scene scene)=>owners.TryGetValue(scene.handle,out var owner)&&owner?owner:null;
    static DiverAudioController Ensure(Scene scene)
    {
        if(!scene.isLoaded || scene.name!="Main")return null;
        var owner=Current(scene);if(owner)return owner;
        foreach(var existing in FindObjectsByType<DiverAudioController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(existing.gameObject.scene==scene){owners[scene.handle]=existing;return existing;}
        var root=new GameObject("DiverAudio");SceneManager.MoveGameObjectToScene(root,scene);
        owner=root.AddComponent<DiverAudioController>();owners[scene.handle]=owner;return owner;
    }
    public static void BeginExperience(Scene scene)
    {
        var owner=Ensure(scene);if(!owner || owner.experienceActive)return;
        owner.experienceActive=true;owner.nextBreath=AudioSettings.dspTime+.1;
    }
    public static void EndExperience(Scene scene)
    {
        var owner=Current(scene);if(!owner)return;
        owner.experienceActive=false;owner.noiseTarget=0;
        Stop(owner.breathSource);Stop(owner.feedingSource);foreach(var source in owner.popupSources)Stop(source);
    }
    public static void SetNoiseLevel(Scene scene,float level)
    {var owner=Current(scene);if(owner){owner.noiseTarget=Mathf.Clamp01(level);owner.lastNoiseUpdate=Time.unscaledTime;}}
    public static void PlayPopup(Scene scene)
    {
        var owner=Current(scene);if(!owner || !owner.CanPlay || AudioSettings.dspTime<owner.nextPopup)return;
        var source=owner.popupSources[owner.nextPopupSource];if(!source || !source.clip)return;
        source.volume=owner.masterVolume*owner.popupVolume;source.Stop();source.Play();
        owner.nextPopupSource=1-owner.nextPopupSource;owner.nextPopup=AudioSettings.dspTime+.07;
    }
    public static void PlayFeeding(Scene scene)
    {
        var owner=Current(scene);if(!owner || !owner.CanPlay || !owner.feedingSource.clip)return;
        owner.feedingSource.volume=owner.masterVolume*owner.feedingVolume;owner.feedingSource.Stop();owner.feedingSource.Play();
    }
    public static void PlayRecognition(Scene scene,bool allowDuringFeeding=false)
    {
        var owner=Current(scene);if(!owner || !owner.CanPlay || AudioSettings.dspTime<owner.nextRecognition)return;
        if(!allowDuringFeeding)
            foreach(var view in FindObjectsByType<DiverViewController>(FindObjectsSortMode.None))
                if(view.gameObject.scene==scene && view.IsFeedingControlLocked)return;
        if(!owner.recognitionSource.clip)return;
        owner.recognitionSource.volume=owner.masterVolume*owner.recognitionVolume;
        owner.recognitionSource.Stop();owner.recognitionSource.Play();owner.nextRecognition=AudioSettings.dspTime+.12;
    }
    bool CanPlay=>isActiveAndEnabled && audioEnabled && experienceActive && !DiverStartupGate.IsBlocked(gameObject.scene);
    void Awake()
    {
        waterSource=Source("InWater",true);breathSource=Source("Breath",false);feedingSource=Source("Feeding",false);
        noiseSource=Source("BorderNoise",true);recognitionSource=Source("Recognition",false);
        popupSources[0]=Source("PopupA",false);popupSources[1]=Source("PopupB",false);
    }
    AudioSource Source(string name,bool loop)
    {
        var root=new GameObject(name);root.transform.SetParent(transform,false);
        var source=root.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=loop;source.spatialBlend=0;source.volume=0;source.dopplerLevel=0;return source;
    }
    IEnumerator Start()
    {
        string[] names={"Breathv2","Feeding","InWater","Noise_Roop","PopUp","Recognision"};
        AudioClip[] clips={breathv2,feeding,inWater,noiseRoop,popup,recognision};
        var requests=new ResourceRequest[clips.Length];
        for(int i=0;i<clips.Length;i++)if(!clips[i])requests[i]=Resources.LoadAsync<AudioClip>("DiverAudio/"+names[i]);
        for(int i=0;i<clips.Length;i++)
        {
            if(requests[i]!=null){yield return requests[i];clips[i]=requests[i].asset as AudioClip;}
            if(!clips[i])Debug.LogWarning("[DiverAudio] Audio clip missing: "+names[i],this);
        }
        breathv2=clips[0];feeding=clips[1];inWater=clips[2];noiseRoop=clips[3];popup=clips[4];recognision=clips[5];
        breathSource.clip=breathv2;feedingSource.clip=feeding;waterSource.clip=inWater;noiseSource.clip=noiseRoop;
        recognitionSource.clip=recognision;foreach(var source in popupSources)source.clip=popup;
        Debug.Log("[DiverAudio] Audio clips loaded. Playback begins when robot startup completes.",this);
    }
    void Update()
    {
        float dt=Mathf.Max(0,Time.unscaledDeltaTime);
        bool active=experienceActive && audioEnabled && !DiverStartupGate.IsBlocked(gameObject.scene);
        gain=Mathf.MoveTowards(gain,active?1:0,dt*2.5f);
        float level=active && Time.unscaledTime-lastNoiseUpdate<.5f ? DiverAudioTiming.NoiseGain(noiseTarget) : 0;
        noiseGain=Mathf.MoveTowards(noiseGain,level,dt*2f);
        UpdateLoop(waterSource,masterVolume*waterVolume*gain);
        UpdateLoop(noiseSource,masterVolume*noiseVolume*noiseGain*gain);
        breathSource.volume=masterVolume*breathVolume*gain;
        if(active && breathSource.clip && !breathSource.isPlaying && AudioSettings.dspTime>=nextBreath)
        {
            breathSource.Play();nextBreath=AudioSettings.dspTime+DiverAudioTiming.BreathPeriod(breathIntervalSeconds,breathSource.clip.length);
        }
        if(!active)
        {
            Stop(breathSource);Stop(feedingSource);foreach(var source in popupSources)Stop(source);
            if(!audioEnabled)Stop(recognitionSource);
        }
    }
    static void UpdateLoop(AudioSource source,float volume)
    {
        source.volume=volume;
        if(volume>.001f && source.clip){if(!source.isPlaying)source.Play();}
        else if(source.isPlaying)source.Stop();
    }
    static void Stop(AudioSource source){if(source)source.Stop();}
    void OnDisable()
    {
        gain=noiseGain=0;Stop(waterSource);Stop(noiseSource);Stop(breathSource);Stop(feedingSource);Stop(recognitionSource);
        foreach(var source in popupSources)Stop(source);
    }
    void OnDestroy()
    {if(owners.TryGetValue(gameObject.scene.handle,out var owner) && owner==this)owners.Remove(gameObject.scene.handle);}
}
