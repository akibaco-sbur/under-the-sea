using System;

/// <summary>Uses Windows wall-clock timestamps; the end timestamp is frozen before shutdown begins.</summary>
internal sealed class WindowsSessionClock
{
    internal bool HasStarted {get;private set;}
    internal bool HasEnded {get;private set;}
    internal DateTimeOffset StartedAt {get;private set;}
    internal DateTimeOffset EndedAt {get;private set;}
    internal void Begin(DateTimeOffset now)
    {if(HasStarted)return;StartedAt=now;HasStarted=true;}
    internal void End(DateTimeOffset now)
    {if(HasEnded)return;Begin(now);EndedAt=now;HasEnded=true;}
    internal TimeSpan Elapsed(DateTimeOffset now)
    {
        if(!HasStarted)return TimeSpan.Zero;
        var span=(HasEnded?EndedAt:now)-StartedAt;
        return span<TimeSpan.Zero?TimeSpan.Zero:span;
    }
    internal static string Format(TimeSpan elapsed)
    {
        long seconds=(long)Math.Max(0,elapsed.TotalSeconds);
        return seconds>=3600 ? (seconds/3600).ToString("00")+":"+((seconds/60)%60).ToString("00")+":"+(seconds%60).ToString("00")
            : (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
    }
}
