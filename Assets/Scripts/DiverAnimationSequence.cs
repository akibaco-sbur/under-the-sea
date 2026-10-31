using System;

internal sealed class DiverAnimationSequence
{
    internal enum Phase { Idle, IdleToSwim, Swim, SwimToIdle, IdleToListen, Listen, ListenToIdle }
    readonly float[] durations;
    bool receivePending;
    internal Phase Current {get;private set;}
    internal float Elapsed {get;private set;}
    internal DiverAnimationSequence(float[] durations) { this.durations=durations; Reset(); }
    internal void Receive()
    {
        // Several commands during one listening gesture never restart its clip.
        if(Current!=Phase.IdleToListen && Current!=Phase.Listen)receivePending=true;
    }
    internal void Reset(){Current=Phase.Idle;Elapsed=0;receivePending=false;}
    internal static float SampleClipTime(float time,float length,bool loop)
    {
        if(length<=0)return 0;
        time=Math.Max(0,time);
        // Stay just inside the final frame even if the imported clip itself loops.
        return loop?time%length:Math.Min(time,Math.Max(0,length-.0001f));
    }
    void Enter(Phase next){Current=next;Elapsed=0;}
    internal void Step(bool swimming,float dt,bool forwardRequested=false)
    {
        if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
        Elapsed+=Math.Min(.2f,dt);
        if(forwardRequested)
        {
            receivePending=false;
            if(Current==Phase.IdleToListen || Current==Phase.Listen)
            {Enter(Phase.ListenToIdle);return;}
        }
        if(Current==Phase.Idle)
        {
            if(swimming)Enter(Phase.IdleToSwim);
            else if(receivePending){receivePending=false;Enter(Phase.IdleToListen);}
            return;
        }
        if(Current==Phase.Swim)
        {
            if(!swimming)Enter(Phase.SwimToIdle);
            return;
        }
        if(Elapsed<Math.Max(.01f,durations[(int)Current]))return;
        switch(Current)
        {
            case Phase.IdleToSwim: Enter(Phase.Swim);break;
            case Phase.SwimToIdle: Enter(Phase.Idle);break;
            case Phase.IdleToListen: Enter(Phase.Listen);break;
            case Phase.Listen: Enter(Phase.ListenToIdle);break;
            case Phase.ListenToIdle: Enter(Phase.Idle);break;
        }
    }
}
