using System;

/// <summary>One piece of food has one successful winner, even for simultaneous arrival callbacks.</summary>
internal sealed class FishFeedingRace
{
    bool cancelled, claiming;
    internal bool HasWinner { get; private set; }
    internal bool TryConsume(Func<bool> applyWinnerColor)
    {
        if (cancelled || claiming || HasWinner) return false;
        claiming = true;
        try
        {
            if (!applyWinnerColor()) return false;
            HasWinner = true;
            return true;
        }
        finally { claiming = false; }
    }
    internal void Cancel() => cancelled = true;
}
