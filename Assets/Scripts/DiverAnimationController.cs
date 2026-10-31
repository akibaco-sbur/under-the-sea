using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>Uses the existing controller's clips, without relying on its placeholder parameters or transitions.</summary>
[DefaultExecutionOrder(-5)]
[DisallowMultipleComponent]
public sealed class DiverAnimationController : MonoBehaviour
{
    static readonly string[] PhaseNames={"Idle","Idle2Swim","Swim","Swim2Idle","Idle2Stop","Stop / radio receive","Stop2Idle"};
    [SerializeField] Animator animator;
    [Header("通常モーション（省略時は既存Animatorから取得）")]
    [SerializeField] AnimationClip idle;
    [SerializeField] AnimationClip swim;
    [SerializeField, InspectorName("受信モーション（Stop）")] AnimationClip listen;
    [Header("遷移モーション")]
    [SerializeField] AnimationClip idleToSwim;
    [SerializeField] AnimationClip swimToIdle;
    [SerializeField] AnimationClip idleToListen;
    [SerializeField] AnimationClip listenToIdle;
    [Header("再生調整")]
    [SerializeField, Min(.05f)] float idleSpeed=.4f;
    [SerializeField, Min(.05f)] float swimSpeed=1f;
    [SerializeField, Min(.05f)] float listenSpeed=1f;
    [SerializeField, Min(.05f)] float transitionSpeed=1.5f;
    [SerializeField, Min(.1f), InspectorName("受信姿勢を保つ最小秒数")] float minimumListenSeconds=1f;
    [SerializeField, Range(.01f,.5f)] float blendSeconds=.12f;
    [SerializeField, InspectorName("現在のモーション（確認用）")] string currentMotion;
    [SerializeField, InspectorName("モーション切替をログ表示")] bool logMotionChanges=true;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable[] players;
    AnimationClip[] clips;
    float[] speeds;
    DiverAnimationSequence sequence;
    RuntimeAnimatorController originalController;
    bool originalRootMotion, configured, paused, ownsAnimator;
    AnimatorCullingMode originalCulling;
    DiverViewController view;
    DiverMovementController movement;
    int currentIndex, previousIndex;
    float blendElapsed, previousTime;

    public static void NotifyRecognizedCommand(Scene scene)
    {
        if(DiverStartupGate.IsBlocked(scene))return;
        foreach(var receiver in FindObjectsByType<DiverAnimationController>(FindObjectsSortMode.None))
            if(receiver.gameObject.scene==scene && receiver.isActiveAndEnabled)receiver.PlayReceiveMotion();
    }
    public void PlayReceiveMotion()
    {
        if(sequence==null || (view && view.IsFeedingControlLocked))return;
        sequence.Receive();
    }
    void Start()=>Initialize();
    void OnEnable(){if(configured)Initialize();}
    AnimationClip FindClip(AnimationClip assigned,string name)
    {
        if(assigned)return assigned;
        foreach(var clip in originalController.animationClips)
        {
            string candidate=clip.name;
            int split=candidate.LastIndexOf('|');if(split>=0)candidate=candidate.Substring(split+1);
            if(string.Equals(candidate,name,StringComparison.OrdinalIgnoreCase))return clip;
        }
        return null;
    }
    void Initialize()
    {
        if(graph.IsValid())return;
        configured=true;view=GetComponent<DiverViewController>();movement=GetComponent<DiverMovementController>();
        if(!animator)animator=GetComponent<Animator>();
        if(!animator)animator=GetComponentInChildren<Animator>();
        if(!animator || !animator.runtimeAnimatorController){Debug.LogWarning("[DiverAnimation] Animator/controller unavailable. Existing animation is unchanged.",this);return;}
        originalController=animator.runtimeAnimatorController;
        idle=FindClip(idle,"Idle_roop");swim=FindClip(swim,"Swim");listen=FindClip(listen,"Stop");
        idleToSwim=FindClip(idleToSwim,"Idle2Swim");swimToIdle=FindClip(swimToIdle,"Swim2Idle");
        idleToListen=FindClip(idleToListen,"Idle2Stop");listenToIdle=FindClip(listenToIdle,"Stop2Idle");
        clips=new[]{idle,idleToSwim,swim,swimToIdle,idleToListen,listen,listenToIdle};
        for(int i=0;i<clips.Length;i++)if(!clips[i]){Debug.LogError("[DiverAnimation] Missing clip for "+(DiverAnimationSequence.Phase)i+". Assign its clip in the Inspector. Existing Animator is unchanged.",this);return;}
        speeds=new[]{Mathf.Max(.05f,idleSpeed),Mathf.Max(.05f,transitionSpeed),Mathf.Max(.05f,swimSpeed),Mathf.Max(.05f,transitionSpeed),Mathf.Max(.05f,transitionSpeed),Mathf.Max(.05f,listenSpeed),Mathf.Max(.05f,transitionSpeed)};
        var durations=new float[clips.Length];for(int i=0;i<clips.Length;i++)durations[i]=clips[i].length/speeds[i];
        durations[5]=Mathf.Max(durations[5],minimumListenSeconds);
        sequence=new DiverAnimationSequence(durations);
        originalRootMotion=animator.applyRootMotion;originalCulling=animator.cullingMode;
        try
        {
            ownsAnimator=true;
            animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            graph=PlayableGraph.Create("DiverMotionSequence");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,clips.Length);
            players=new AnimationClipPlayable[clips.Length];
            for(int i=0;i<clips.Length;i++)
            {
                players[i]=AnimationClipPlayable.Create(graph,clips[i]);players[i].SetApplyFootIK(false);players[i].SetSpeed(0);
                graph.Connect(players[i],0,mixer,i);mixer.SetInputWeight(i,i==0?1:0);
            }
            var output=AnimationPlayableOutput.Create(graph,"DiverMotion",animator);output.SetSourcePlayable(mixer);
            currentIndex=previousIndex=0;blendElapsed=blendSeconds;previousTime=0;paused=false;
            graph.Play();
            Debug.Log("[DiverAnimation] Connected Idle / radio receive (Stop) / Swim with all four transition clips.",this);
            for(int i=0;i<clips.Length;i++)Debug.Log("[DiverAnimation] "+PhaseNames[i]+" = "+clips[i].name+" ("+clips[i].length.ToString("F2")+" s)",this);
        }
        catch(Exception exception){Release();Debug.LogError("[DiverAnimation] Setup failed: "+exception.Message,this);}
    }
    void Update()
    {
        if(!graph.IsValid() || !animator)return;
        // Animator disabling is the existing feeding pause: never overwrite tracked arm bones.
        if(!animator.enabled || (view && view.IsFeedingControlLocked))
        {sequence.Reset();paused=true;currentMotion="Feeding / paused";return;}
        if(paused)
        {
            currentIndex=previousIndex=0;blendElapsed=blendSeconds;previousTime=0;paused=false;
            for(int i=0;i<players.Length;i++)mixer.SetInputWeight(i,i==0?1:0);
        }
        float dt=Mathf.Max(0,Mathf.Min(.2f,Time.deltaTime));
        bool controlsReady=!DiverStartupGate.IsBlocked(gameObject.scene) && movement && movement.isActiveAndEnabled;
        bool forwardRequested=controlsReady && (movement.IsMoving || movement.IsAligningToMove);
        bool swimming=forwardRequested || (controlsReady && movement.CurrentSpeed>.02f);
        sequence.Step(swimming,dt,forwardRequested);
        int next=(int)sequence.Current;
        currentMotion=PhaseNames[next];
        if(next!=currentIndex)
        {
            if(logMotionChanges)Debug.Log("[DiverAnimation] "+PhaseNames[currentIndex]+" -> "+PhaseNames[next]+"; forward="+forwardRequested+", speed="+(movement?movement.CurrentSpeed:0).ToString("F2"),this);
            previousIndex=currentIndex;previousTime=(float)players[previousIndex].GetTime();currentIndex=next;blendElapsed=0;
        }
        float currentTime=sequence.Elapsed*speeds[currentIndex];
        bool loop=currentIndex==0||currentIndex==2;
        players[currentIndex].SetTime(DiverAnimationSequence.SampleClipTime(currentTime,clips[currentIndex].length,loop));
        blendElapsed+=dt;float blend=Mathf.Clamp01(blendElapsed/Mathf.Max(.01f,blendSeconds));
        for(int i=0;i<players.Length;i++)mixer.SetInputWeight(i,0);
        if(previousIndex!=currentIndex && blend<1)
        {
            previousTime+=dt*speeds[previousIndex];
            bool previousLoop=previousIndex==0||previousIndex==2;
            players[previousIndex].SetTime(DiverAnimationSequence.SampleClipTime(previousTime,clips[previousIndex].length,previousLoop));
            mixer.SetInputWeight(previousIndex,1-blend);
        }
        mixer.SetInputWeight(currentIndex,previousIndex==currentIndex?1:blend);
        graph.Evaluate(0);
    }
    void Release()
    {
        if(graph.IsValid())graph.Destroy();
        if(ownsAnimator && animator)
        {animator.runtimeAnimatorController=originalController;animator.applyRootMotion=originalRootMotion;animator.cullingMode=originalCulling;}
        ownsAnimator=false;
        sequence=null;
    }
    void OnDisable()=>Release();
    void OnDestroy()=>Release();
}
