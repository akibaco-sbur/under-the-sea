using UnityEngine;
using UnityEngine.UI;

internal static class DiverRouteReviewSkin
{
    static readonly Color cyan=new Color(.2f,.92f,1,1);
    internal static RectTransform Build(Transform canvas,Font font,WindowsSessionClock clock,double distance,int samples,Bounds bounds,float gridStep)
    {
        var frame=Rect("ArchiveFrame",canvas,new Vector2(.018f,.025f),new Vector2(.982f,.975f));
        var graphic=frame.gameObject.AddComponent<DiverRobotFrameGraphic>();graphic.color=new Color(.12f,.75f,.85f,.5f);graphic.raycastTarget=false;
        var heading=Text(canvas,font,"DIVER  //  SESSION ARCHIVE",new Vector2(.045f,.905f),new Vector2(.75f,.96f),34,cyan);
        heading.alignment=TextAnchor.MiddleLeft;
        Text(canvas,font,"航行記録を復元 / ROUTE RECONSTRUCTION",new Vector2(.045f,.86f),new Vector2(.75f,.9f),16,new Color(.48f,.68f,.73f));
        Text(canvas,font,"RECORD CLOSED",new Vector2(.76f,.91f),new Vector2(.955f,.96f),18,new Color(1,.8f,.2f));
        var timing=Panel(canvas,"SessionClock",new Vector2(.045f,.55f),new Vector2(.24f,.83f));
        Text(timing,font,"01  //  WINDOWS CLOCK",new Vector2(.06f,.79f),new Vector2(.94f,.96f),17,cyan);
        Text(timing,font,"開始  START\n"+clock.StartedAt.ToLocalTime().ToString("yyyy.MM.dd  HH:mm:ss")+"\n\n終了  END\n"+clock.EndedAt.ToLocalTime().ToString("yyyy.MM.dd  HH:mm:ss"),new Vector2(.06f,.06f),new Vector2(.94f,.76f),18,new Color(.82f,.94f,.97f));
        var elapsed=Panel(canvas,"ExperienceDuration",new Vector2(.045f,.25f),new Vector2(.24f,.51f));
        Text(elapsed,font,"02  //  EXPERIENCE TIME",new Vector2(.06f,.78f),new Vector2(.94f,.95f),17,cyan);
        var duration=Text(elapsed,font,WindowsSessionClock.Format(clock.Elapsed(clock.EndedAt)),new Vector2(.06f,.38f),new Vector2(.94f,.75f),42,Color.white);duration.alignment=TextAnchor.MiddleLeft;
        Text(elapsed,font,"体験時間\n終了時刻 − 開始時刻",new Vector2(.06f,.06f),new Vector2(.94f,.35f),17,new Color(.48f,.68f,.73f));
        var metrics=Panel(canvas,"NavigationRecord",new Vector2(.76f,.55f),new Vector2(.955f,.83f));
        Text(metrics,font,"03  //  NAVIGATION DATA",new Vector2(.06f,.79f),new Vector2(.94f,.96f),17,cyan);
        Text(metrics,font,$"移動距離\n{distance:0.0} m\n\n記録点数   {samples:N0}",new Vector2(.06f,.07f),new Vector2(.94f,.76f),24,new Color(.82f,.94f,.97f));
        var survey=Panel(canvas,"SurveyExtent",new Vector2(.76f,.25f),new Vector2(.955f,.51f));
        Text(survey,font,"04  //  SURVEY GRID",new Vector2(.06f,.79f),new Vector2(.94f,.96f),17,cyan);
        Text(survey,font,$"SeaBed全体\n{bounds.size.x:0.#} × {bounds.size.z:0.#} m\n1マス  {gridStep:0.##} m\n\nN ↑  /  北が上",new Vector2(.06f,.07f),new Vector2(.94f,.76f),20,new Color(.82f,.94f,.97f));
        Text(canvas,font,"SPACE  /  もう一度再生",new Vector2(.76f,.12f),new Vector2(.955f,.2f),17,cyan);
        Text(canvas,font,"CONTROL INPUT LOCKED\nROUTE RECORD FINALIZED",new Vector2(.045f,.12f),new Vector2(.24f,.2f),14,new Color(.48f,.68f,.73f));
        var track=Rect("PlaybackTrack",canvas,new Vector2(.28f,.168f),new Vector2(.72f,.174f));Fill(track,new Color(.1f,.4f,.46f,.5f));
        var progress=Rect("PlaybackProgress",track,Vector2.zero,new Vector2(0,1));Fill(progress,cyan);
        return progress;
    }
    static RectTransform Panel(Transform root,string name,Vector2 min,Vector2 max)
    {
        var r=Rect(name,root,min,max);Fill(r,new Color(.02f,.065f,.085f,.94f));
        var bar=Rect("Accent",r,new Vector2(0,.985f),Vector2.one);Fill(bar,cyan);
        var side=Rect("Edge",r,Vector2.zero,new Vector2(.004f,1));Fill(side,new Color(.12f,.65f,.74f,.6f));return r;
    }
    static Text Text(Transform root,Font font,string content,Vector2 min,Vector2 max,int size,Color tint)
    {
        var r=Rect("Readout",root,min,max);var t=r.gameObject.AddComponent<Text>();t.font=font;t.text=content;t.fontSize=size;t.color=tint;t.raycastTarget=false;t.alignment=TextAnchor.UpperLeft;return t;
    }
    static void Fill(RectTransform rect,Color tint){var i=rect.gameObject.AddComponent<Image>();i.color=tint;i.raycastTarget=false;}
    static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return r;
    }
}
