using System;

internal sealed class BorderIdleRetreat
{
    float anchorX,anchorZ;
    double stoppedAt;
    bool fired;
    internal bool Armed {get;private set;}
    internal void Arm(float x,float z,double now)
    {anchorX=x;anchorZ=z;stoppedAt=now;Armed=true;fired=false;}
    internal float Remaining(double now,float seconds)=>(float)Math.Max(0,Math.Max(1,seconds)-Math.Max(0,now-stoppedAt));
    internal bool Tick(float x,float z,double now,float seconds)
    {
        if(!Armed || fired || double.IsNaN(now) || double.IsInfinity(now))return false;
        double dx=(double)x-anchorX,dz=(double)z-anchorZ;
        if(dx*dx+dz*dz>.05*.05){Reset();return false;}
        if(now<stoppedAt){stoppedAt=now;return false;}
        if(Remaining(now,seconds)>0)return false;
        fired=true;return true;
    }
    internal void Reset(){Armed=false;fired=false;stoppedAt=0;}
}
