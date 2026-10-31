using System;

internal static class DiverRouteGrid
{
    // About ten squares along the longer side, with readable 1/2/5 metre multiples.
    internal static float Step(float width,float height)
    {
        double span=Math.Max(.01,Math.Max(width,height));
        double target=span/10;
        double power=Math.Pow(10,Math.Floor(Math.Log10(target)));
        double value=target/power;
        return (float)((value<=1?1:value<=2?2:value<=5?5:10)*power);
    }
}
