using System;

internal static class DiverProjectionMath
{
    internal static float Fit(float width,float height,float areaWidth,float areaHeight,float scale)
    {
        if(float.IsNaN(scale)||float.IsInfinity(scale))scale=1;
        float result=Math.Min(width*areaWidth/1920f,height*areaHeight/1080f)*Math.Max(.25f,Math.Min(1.5f,scale));
        return float.IsNaN(result)||float.IsInfinity(result)?1:Math.Max(.001f,result);
    }
}
