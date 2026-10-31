using System;

internal sealed class BorderInterferenceState
{
    internal const int MaximumPopups=15;
    readonly Random random;
    internal float Strength {get;private set;}
    internal int PopupCount {get;private set;}
    internal float NoiseSeed {get;private set;}
    internal float NoiseBurst {get;private set;}
    float popupClock, popupDelay, noiseClock;
    internal BorderInterferenceState(int? seed=null)
    {random=new Random(seed??Guid.NewGuid().GetHashCode());Reset();}
    float Next(float min,float max)=>min+(max-min)*(float)random.NextDouble();
    internal static float Proximity(float distance,float startDistance=150,float fullDistance=20)
    {
        if(float.IsNaN(distance))return 0;
        fullDistance=Math.Max(0,fullDistance);
        startDistance=Math.Max(fullDistance+.1f,startDistance);
        return Math.Max(0,Math.Min(1,(startDistance-distance)/(startDistance-fullDistance)));
    }
    internal void Tick(float target,float dt)
    {
        if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
        if(float.IsNaN(target))target=0;
        target=Math.Max(0,Math.Min(1,target));
        float step=Math.Min(.1f,dt)*(target>Strength?2f:3f);
        Strength=target>Strength?Math.Min(target,Strength+step):Math.Max(target,Strength-step);
        noiseClock-=Math.Min(.1f,dt);
        if(noiseClock<=0)
        {
            NoiseSeed=Next(1,8192);
            bool quiet=random.NextDouble()<.15;
            noiseClock=quiet?Next(.2f,.9f):Next(.025f,.21f);
            NoiseBurst=quiet?.06f:random.NextDouble()<.3?Next(.75f,1f):Next(.15f,.5f);
        }
        int desired=Strength<.1f?0:Math.Min(MaximumPopups,1+(int)((Strength-.1f)/.9f*MaximumPopups));
        if(desired<PopupCount){PopupCount=desired;popupClock=0;}
        else if(desired>PopupCount)
        {
            popupClock+=Math.Min(.1f,dt);
            if(popupClock>=popupDelay){PopupCount++;popupClock=0;popupDelay=Next(.07f,.55f);}
        }
        else popupClock=0;
    }
    internal void Reset(){Strength=0;PopupCount=0;popupClock=0;popupDelay=Next(.12f,.5f);noiseClock=0;NoiseSeed=0;NoiseBurst=0;}
}
