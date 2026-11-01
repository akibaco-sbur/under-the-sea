using System;

internal static class VoskMicrophoneSelection
{
    internal static bool TrySelect(string[] devices,string preferred,out string selected,out string error)
    {
        selected=null;error=null;
        if(devices==null||devices.Length==0){error="No microphone is available.";return false;}
        string target=string.IsNullOrWhiteSpace(preferred)?"USB":preferred.Trim();
        int exact=0,partial=0;string exactName=null,partialName=null;
        foreach(var device in devices)
        {
            if(string.IsNullOrEmpty(device))continue;
            if(string.Equals(device,target,StringComparison.OrdinalIgnoreCase)){exact++;exactName=device;}
            if(device.IndexOf(target,StringComparison.OrdinalIgnoreCase)>=0){partial++;partialName=device;}
        }
        if(exact==1){selected=exactName;return true;}
        if(exact==0 && partial==1){selected=partialName;return true;}
        error=exact>1||partial>1 ? "Multiple microphones match '"+target+"'. Specify the complete unique device name."
            : "Configured microphone '"+target+"' was not found. Set its name from the available microphone list.";
        return false;
    }
    internal static int InputRate(int requested,int minimum,int maximum)
    {
        int rate=Math.Max(8000,Math.Min(48000,requested));
        if(minimum>0)rate=Math.Max(minimum,rate);
        if(maximum>0)rate=Math.Min(maximum,rate);
        return rate;
    }
}
