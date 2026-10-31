using System;
using System.Collections.Generic;

/// <summary>Bounded session path. Compaction preserves both ends of the whole trip.</summary>
public sealed class DiverPathRecording
{
    public struct Point
    {
        public float X, Y, Z;
        public Point(float x,float y,float z) { X=x; Y=y; Z=z; }
    }
    readonly List<Point> points = new List<Point>();
    readonly int capacity;
    float spacing;
    Point lastSample;
    bool hasSample;
    public IReadOnlyList<Point> Points => points;
    public double Distance { get; private set; }
    public DiverPathRecording(int capacity=4096,float spacing=.25f)
    {
        this.capacity=Math.Max(4,Math.Min(4096,capacity));
        this.spacing=Math.Max(.01f,spacing);
    }
    public void Sample(float x,float y,float z,bool force=false)
    {
        if(float.IsNaN(x)||float.IsNaN(y)||float.IsNaN(z)||float.IsInfinity(x)||float.IsInfinity(y)||float.IsInfinity(z))return;
        var p=new Point(x,y,z);
        if(hasSample) Distance+=Length(lastSample,p);
        lastSample=p; hasSample=true;
        if(points.Count>0 && Length(points[points.Count-1],p)<(force?.001f:spacing))return;
        if(points.Count==capacity)
        {
            int write=1;
            for(int read=2;read<points.Count-1;read+=2)points[write++]=points[read];
            points[write++]=points[points.Count-1];
            points.RemoveRange(write,points.Count-write);
            spacing*=2;
        }
        points.Add(p);
    }
    static double Length(Point a,Point b)
    { double x=(double)a.X-b.X,y=(double)a.Y-b.Y,z=(double)a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }
}
