using System;

internal static class DiverAudioTiming
{
    internal static float NoiseGain(float intensity)
    {
        if(float.IsNaN(intensity))return 0;
        float strength=Math.Max(0,Math.Min(1,(intensity-.2f)/.8f));return strength*strength;
    }
    internal static double BreathPeriod(float interval,float clipLength)
        => Math.Max(.5,Math.Max(interval,clipLength+.05f));
}
