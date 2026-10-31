using System;

internal static class DiverObstacleBraking
{
    internal static float LookAhead(float speed,float nextSpeed,float dt,float deceleration,float clearance)
    {
        float peak=Math.Max(0,Math.Max(speed,nextSpeed));
        return Math.Max(0,clearance)+peak*peak/(2*Math.Max(.01f,deceleration))+peak*Math.Max(0,dt)+.05f;
    }
    internal static float LimitTravel(float speed,float dt,float available)
        => ResolveTravel(Math.Max(0,speed)*Math.Max(0,dt),available,out _);

    internal static float ResolveTravel(float requested,float available,out bool stoppedAtBoundary)
    {
        requested=Math.Max(0,requested);
        available=Math.Max(0,available);
        // Determine restriction from the obstacle boundary, not by comparing a
        // rounded return value against a freshly evaluated speed*dt expression.
        stoppedAtBoundary=!float.IsPositiveInfinity(available) && requested>=available;
        return Math.Min(requested,available);
    }
}
