using System;

/// <summary>Display animation only; never reports fabricated sensor/model readiness.</summary>
internal sealed class RobotHudBootSequence
{
    readonly float duration;
    float elapsed;
    internal float Progress => Math.Min(1f,elapsed/duration);
    internal bool Complete => Progress>=1f;
    internal bool CanReleaseControls(bool playerValid) => Complete && playerValid;
    internal float OpticsOpen => Reveal(.08f,.48f);
    internal float FrameAlpha => Reveal(.12f,.33f);
    internal float HeadingAlpha => Reveal(.3f,.48f);
    internal float SystemsAlpha => Reveal(.46f,.65f);
    internal float DetailsAlpha => Reveal(.62f,.8f);
    internal float GuideAlpha => Reveal(.82f,.97f);
    internal float CaptionAlpha => 1f-Reveal(.91f,1f);
    internal string Caption => Progress<.3f ? "視界を起動しています" : Progress<.55f ? "方位ディスプレイを展開中" :
        Progress<.82f ? "操作ディスプレイを展開中" : "操作を開始します";
    internal RobotHudBootSequence(float seconds) { duration=Math.Max(1f,seconds); }
    internal void Step(float dt)
    {
        if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0f)return;
        elapsed+=Math.Min(.2f,dt);
    }
    float Reveal(float start,float end)
    {
        float t=Math.Max(0,Math.Min(1,(Progress-start)/(end-start)));
        return t*t*(3f-2f*t);
    }
}
