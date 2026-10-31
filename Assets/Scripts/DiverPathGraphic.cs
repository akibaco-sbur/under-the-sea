using UnityEngine;
using UnityEngine.UI;

/// <summary>At most 4096 samples: a single UI mesh stays below the vertex limit.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DiverPathGraphic : MaskableGraphic
{
    Vector2[] points = new Vector2[0];
    float progress;
    public void SetPath(Vector2[] value) { points=value; SetVerticesDirty(); }
    public void SetProgress(float value) { progress=Mathf.Clamp01(value); SetVerticesDirty(); }
    Vector2 Position(Vector2 p) => rectTransform.rect.min + Vector2.Scale(p,rectTransform.rect.size);
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if(points.Length==0)return;
        float end=progress*Mathf.Max(0,points.Length-1);
        int full=Mathf.FloorToInt(end);
        for(int i=0;i<points.Length-1 && i<=full;i++)
        {
            Vector2 a=Position(points[i]), b=Position(points[i+1]);
            if(i==full)b=Vector2.Lerp(a,b,end-full);
            if((b-a).sqrMagnitude<.001f)continue;
            Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*2f;
            Quad(vh,a-n,a+n,b+n,b-n,color);
        }
        Mark(vh,Position(points[0]),new Color(.2f,.7f,.95f),5);
        Vector2 head=Position(points[full]);
        if(full<points.Length-1)head=Vector2.Lerp(head,Position(points[full+1]),end-full);
        Mark(vh,head,new Color(.05f,.35f,.6f),6);
        if(progress>=1)Mark(vh,Position(points[points.Length-1]),new Color(.05f,.35f,.6f),6);
    }
    static void Mark(VertexHelper vh,Vector2 p,Color c,float r) => Quad(vh,p+new Vector2(-r,-r),p+new Vector2(-r,r),p+new Vector2(r,r),p+new Vector2(r,-r),c);
    static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color tint)
    {
        int i=vh.currentVertCount;
        vh.AddVert(a,tint,Vector2.zero); vh.AddVert(b,tint,Vector2.zero);
        vh.AddVert(c,tint,Vector2.zero); vh.AddVert(d,tint,Vector2.zero);
        vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
    }
}
