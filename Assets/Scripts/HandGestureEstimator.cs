using System;

// Experimental depth-silhouette heuristic. The first visible pose is assumed open.
internal sealed class HandGestureEstimator
{
    internal enum Pose { Unknown, Open, Closed }
    private float filteredArea, openArea;
    private double startedAt, lastTime, candidateSince;
    private bool initialized;
    private Pose candidate;
    internal Pose Current { get; private set; }
    internal float FilteredArea => filteredArea;
    internal float OpenArea => openArea;
    internal float Ratio => openArea > 0 ? filteredArea / openArea : 0;
    internal Pose Candidate => candidate;
    internal double CandidateSeconds(double time) => Math.Max(0, time - candidateSince);
    internal const float GrabThreshold = 0.88f;
    internal const float ReleaseThreshold = 0.96f;

    internal void ResetTracking()
    {
        initialized = false;
        Current = candidate = Pose.Unknown;
    }

    internal void PauseTracking(double time) { candidate = Current; candidateSince = time; }

    internal Pose Push(float area, double time, bool moving)
    {
        if (!float.IsFinite(area) || area <= 0) { PauseTracking(time); return Pose.Unknown; }
        if (!initialized)
        {
            initialized = true;
            filteredArea = openArea = area;
            startedAt = lastTime = time;
            return Current;
        }
        double dt = Math.Max(0, Math.Min(0.2, time - lastTime));
        lastTime = time;
        float filter = (float)(1 - Math.Exp(-dt * 12));
        filteredArea += (area - filteredArea) * filter;
        if (Current == Pose.Unknown)
        {
            // Establish an open-hand baseline automatically while the user starts moving.
            openArea += (filteredArea - openArea) * filter;
            if (time - startedAt >= 0.4) { Current = candidate = Pose.Open; candidateSince = time; }
            return Current;
        }
        if (moving)
        {
            // Adapt the open reference to changes in perspective while the hand travels.
            // A moving hand is not itself evidence of a grab or release.
            if (Current == Pose.Open) openArea += (filteredArea - openArea) * (float)(1 - Math.Exp(-dt * 2));
            candidate = Current; candidateSince = time;
            return Current;
        }
        if (Current == Pose.Open && filteredArea >= openArea * 0.97f)
        {
            float rate = filteredArea > openArea ? 1.5f : 0.3f;
            openArea += (filteredArea - openArea) * (float)(1 - Math.Exp(-dt * rate));
        }
        Pose next = Current == Pose.Open && filteredArea < openArea * GrabThreshold ? Pose.Closed :
            Current == Pose.Closed && filteredArea > openArea * ReleaseThreshold ? Pose.Open : Current;
        if (candidate != next) { candidate = next; candidateSince = time; }
        if (time - candidateSince >= 0.2) Current = next;
        return Current;
    }
}
