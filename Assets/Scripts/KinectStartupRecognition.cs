using System;

/// <summary>Requires fresh frames from one body continuously; repeated/stale frames never advance time.</summary>
internal sealed class KinectStartupRecognition
{
    readonly double seconds, maximumGap;
    bool hasBody;
    long lastFrame = -1;
    double lastFrameTime, elapsed;
    internal uint BodyId { get; private set; }
    internal bool Complete { get; private set; }
    internal float Progress => (float)Math.Min(1, elapsed / seconds);
    internal KinectStartupRecognition(double seconds = 3, double maximumGap = .35)
    { this.seconds = Math.Max(.1, seconds); this.maximumGap = Math.Max(.01, maximumGap); }

    internal void Tick(bool valid, uint bodyId, long frame, double frameAge, double now)
    {
        if (Complete) return;
        if (!valid || frame < 0 || double.IsNaN(frameAge) || frameAge < 0 || frameAge > maximumGap || double.IsNaN(now) || double.IsInfinity(now))
        { Reset(); return; }
        if (!hasBody || BodyId != bodyId || now < lastFrameTime || now - lastFrameTime > maximumGap || frame < lastFrame)
        {
            hasBody = true; BodyId = bodyId; lastFrame = frame; lastFrameTime = now; elapsed = 0;
            return;
        }
        if (frame == lastFrame) return;
        elapsed += now - lastFrameTime;
        lastFrameTime = now; lastFrame = frame;
        Complete = elapsed >= seconds;
    }
    internal void Reset()
    { hasBody = false; BodyId = 0; lastFrame = -1; lastFrameTime = elapsed = 0; Complete = false; }
}

/// <summary>Seated players may have occluded pelvis/legs; only visible upper-body landmarks are required.</summary>
internal static class KinectStartupBodyValidation
{
    static readonly int[] UpperBodyJoints = { 2, 5, 12, 26 }; // chest, left/right shoulder, head
    internal static bool IsUpperBodyValid(AzureKinectNative.Joint[] joints)
    {
        if (joints == null || joints.Length <= 26) return false;
        foreach (int index in UpperBodyJoints)
        {
            var joint = joints[index];
            if (joint.Confidence < 2 || !float.IsFinite(joint.X) || !float.IsFinite(joint.Y) ||
                !float.IsFinite(joint.Z) || joint.Z <= 0f) return false;
        }
        return true;
    }
}
