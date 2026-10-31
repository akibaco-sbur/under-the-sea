using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class DiverHudCompassGraphic : MaskableGraphic
{
    readonly UIVertex[] quad = new UIVertex[4];
    float heading;
    public void SetHeading(float value)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(heading, value)) < 0.1f) return;
        heading = value;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect bounds = rectTransform.rect;
        const float pixelsPerDegree = 5f;
        for (int degree = 0; degree < 360; degree += 5)
        {
            float x = Mathf.DeltaAngle(heading, degree) * pixelsPerDegree;
            if (x < bounds.xMin + 1f || x > bounds.xMax - 1f) continue;
            float height = degree % 15 == 0 ? 15f : 7f;
            AddRect(helper, x, bounds.yMin, 1f, height, color);
        }
        AddRect(helper, -1f, bounds.yMin - 3f, 2f, 23f, new Color(color.r, color.g, color.b, 1f));
    }
    void AddRect(VertexHelper helper, float x, float y, float width, float height, Color tint)
    {
        for (int i = 0; i < 4; i++) { quad[i] = UIVertex.simpleVert; quad[i].color = tint; }
        quad[0].position = new Vector3(x, y, 0f);
        quad[1].position = new Vector3(x, y + height, 0f);
        quad[2].position = new Vector3(x + width, y + height, 0f);
        quad[3].position = new Vector3(x + width, y, 0f);
        helper.AddUIVertexQuad(quad);
    }
}
