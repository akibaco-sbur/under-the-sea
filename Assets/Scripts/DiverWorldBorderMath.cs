using System;

internal static class DiverWorldBorderMath
{
    internal static float Clamp(float value,float min,float max)=>Math.Min(max,Math.Max(min,value));
    internal static float Distance(float x,float z,float dx,float dz,float minX,float maxX,float minZ,float maxZ)
    {
        if(x<minX || x>maxX || z<minZ || z>maxZ)return 0;
        float distance=float.PositiveInfinity;
        if(dx>1e-6f)distance=Math.Min(distance,(maxX-x)/dx);
        else if(dx< -1e-6f)distance=Math.Min(distance,(minX-x)/dx);
        if(dz>1e-6f)distance=Math.Min(distance,(maxZ-z)/dz);
        else if(dz< -1e-6f)distance=Math.Min(distance,(minZ-z)/dz);
        return Math.Max(0,distance);
    }
}
