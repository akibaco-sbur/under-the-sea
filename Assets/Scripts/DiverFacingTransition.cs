using System;

/// <summary>Transfers head yaw into body yaw while keeping the viewed world direction stable.</summary>
internal sealed class DiverFacingTransition
{
    float elapsed, angle;
    internal float Fraction { get; private set; }
    internal float BodyYaw => angle * Fraction;
    internal float RemainingYaw => angle * (1f - Fraction);
    internal bool Complete { get; private set; }
    internal void Begin(float yawAngle)
    { angle = yawAngle; elapsed = Fraction = 0f; Complete = false; }
    internal void Step(float dt, float duration, float pitch)
    {
        if (Complete || float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) return;
        elapsed += dt;
        float t = Math.Abs(angle) < .1f ? 1f : Math.Min(1f, elapsed / Math.Max(.01f,duration));
        Fraction = t*t*(3f-2f*t);
        Complete = t >= 1f && Math.Abs(pitch) <= .2f;
    }
}
