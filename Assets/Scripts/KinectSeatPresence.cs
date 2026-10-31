using System;

/// <summary>Tracks the original seated visitor, allowing brief occlusion but never switching visitors.</summary>
internal sealed class KinectSeatPresence
{
    readonly double lostSeconds;
    readonly float maximumRiseMm;
    uint bodyId;
    float seatedChestY;
    double invalidSince = -1;
    internal bool Ended { get; private set; }
    internal KinectSeatPresence(double lostSeconds=1.5, float maximumRiseMm=300)
    { this.lostSeconds=Math.Max(.1,lostSeconds); this.maximumRiseMm=Math.Max(100,maximumRiseMm); }
    internal void Begin(uint id,float chestY)
    { bodyId=id;seatedChestY=chestY;invalidSince=-1;Ended=false; }
    internal bool Tick(bool tracked,uint id,float chestY,double now)
    {
        if(Ended)return true;
        if(double.IsNaN(now)||double.IsInfinity(now))return false;
        // Azure Kinect Y grows downward: standing up decreases chest Y.
        bool seated=tracked && id==bodyId && !float.IsNaN(chestY) && !float.IsInfinity(chestY) && seatedChestY-chestY<maximumRiseMm;
        if(seated)invalidSince=-1;
        else if(invalidSince<0 || now<invalidSince)invalidSince=now;
        else if(now-invalidSince>=lostSeconds)Ended=true;
        return Ended;
    }
}
