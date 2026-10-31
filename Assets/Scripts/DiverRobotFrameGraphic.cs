using UnityEngine;
using UnityEngine.UI;

/// <summary>Helmet perimeter, optical brackets and side scales; keeps the central scene visible.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DiverRobotFrameGraphic : MaskableGraphic
{
    readonly UIVertex[] quad=new UIVertex[4];
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();Rect rect=rectTransform.rect;float left=rect.xMin+22,right=rect.xMax-22,top=rect.yMax-22,bottom=rect.yMin+22;
        foreach(float x in new[]{left,right})foreach(float y in new[]{top,bottom})
        {
            float sx=x==left?1:-1,sy=y==bottom?1:-1;
            Line(helper,new Vector2(x,y),new Vector2(x+sx*74,y),2,color);
            Line(helper,new Vector2(x,y),new Vector2(x,y+sy*54),2,color);
        }
        Color dim=new Color(color.r,color.g,color.b,color.a*.35f);
        foreach(float x in new[]{left+8,right-8})
        {
            Line(helper,new Vector2(x,bottom+170),new Vector2(x,top-200),1,dim);
            for(float y=bottom+180;y<top-190;y+=36)
                Line(helper,new Vector2(x,y),new Vector2(x+(x<0?9:-9),y),1,dim);
        }
        // Four small brackets, rather than an opaque central crosshair.
        foreach(float x in new[]{-33f,33f})foreach(float y in new[]{-22f,22f})
        {
            Line(helper,new Vector2(x,y),new Vector2(x+(x<0?10:-10),y),1,dim);
            Line(helper,new Vector2(x,y),new Vector2(x,y+(y<0?7:-7)),1,dim);
        }
    }
    void Line(VertexHelper helper,Vector2 a,Vector2 b,float width,Color tint)
    {
        Vector2 direction=(b-a).normalized,normal=new Vector2(-direction.y,direction.x)*width*.5f;
        for(int i=0;i<4;i++){quad[i]=UIVertex.simpleVert;quad[i].color=tint;}
        quad[0].position=a-normal;quad[1].position=a+normal;quad[2].position=b+normal;quad[3].position=b-normal;
        helper.AddUIVertexQuad(quad);
    }
}
