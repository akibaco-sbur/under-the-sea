using UnityEngine;
using UnityEngine.UI;

/// <summary>A quiet seated-person illustration for the white start screen.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KinectStartupFigure : MaskableGraphic
{
    readonly UIVertex[] quad = new UIVertex[4];
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Vector2 center = rectTransform.rect.center;
        float scale = Mathf.Min(rectTransform.rect.width/190f, rectTransform.rect.height/240f);
        // Chair back, seat and legs, behind the person.
        Line(helper,center+new Vector2(-53,45)*scale,center+new Vector2(53,45)*scale,4*scale);
        Line(helper,center+new Vector2(-53,45)*scale,center+new Vector2(-53,-34)*scale,4*scale);
        Line(helper,center+new Vector2(53,45)*scale,center+new Vector2(53,-34)*scale,4*scale);
        Line(helper,center+new Vector2(-60,-34)*scale,center+new Vector2(60,-34)*scale,5*scale);
        Line(helper,center+new Vector2(-53,-34)*scale,center+new Vector2(-53,-105)*scale,4*scale);
        Line(helper,center+new Vector2(53,-34)*scale,center+new Vector2(53,-105)*scale,4*scale);
        Circle(helper,center+new Vector2(0,83)*scale,22*scale);
        Line(helper,center+new Vector2(0,48)*scale,center+new Vector2(0,-28)*scale,19*scale);
        Line(helper,center+new Vector2(-4,40)*scale,center+new Vector2(-33,-24)*scale,11*scale);
        Line(helper,center+new Vector2(4,40)*scale,center+new Vector2(33,-24)*scale,11*scale);
        Line(helper,center+new Vector2(-4,-22)*scale,center+new Vector2(-28,-52)*scale,13*scale);
        Line(helper,center+new Vector2(4,-22)*scale,center+new Vector2(28,-52)*scale,13*scale);
        Line(helper,center+new Vector2(-28,-52)*scale,center+new Vector2(-28,-99)*scale,12*scale);
        Line(helper,center+new Vector2(28,-52)*scale,center+new Vector2(28,-99)*scale,12*scale);
    }
    void Line(VertexHelper helper,Vector2 a,Vector2 b,float width)
    {
        Vector2 direction=(b-a).normalized, normal=new Vector2(-direction.y,direction.x)*width*.5f;
        Vector2[] points={a-normal,a+normal,b+normal,b-normal};
        for(int i=0;i<4;i++){quad[i]=UIVertex.simpleVert;quad[i].color=color;quad[i].position=points[i];}
        helper.AddUIVertexQuad(quad);
    }
    void Circle(VertexHelper helper,Vector2 center,float radius)
    {
        int start=helper.currentVertCount;
        helper.AddVert(center,color,Vector2.zero);
        const int segments=40;
        for(int i=0;i<=segments;i++)
        {
            float angle=i*Mathf.PI*2/segments;
            helper.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);
            if(i>0)helper.AddTriangle(start,start+i,start+i+1);
        }
    }
}
