using System;
using System.Globalization;

/// <summary>Bounded aggregate of input levels; stores no audio samples.</summary>
internal sealed class VoskAudioProbe
{
    double rawSquares, monoSquares;
    long samples, frames;
    float peak;
    internal double RawDb => Db(samples == 0 ? 0 : rawSquares/samples);
    internal double MonoDb => Db(frames == 0 ? 0 : monoSquares/frames);
    internal bool HasSamples => frames > 0;
    internal void Observe(float[] input, int channels)
    {
        if(input == null || channels <= 0)return;
        for(int i=0;i+channels<=input.Length;i+=channels)
        {
            double mono=0;
            for(int c=0;c<channels;c++)
            {
                float value=input[i+c];
                if(float.IsNaN(value)||float.IsInfinity(value))value=0;
                rawSquares+=(double)value*value; samples++;
                peak=Math.Max(peak,Math.Abs(value));mono+=value/channels;
            }
            monoSquares+=mono*mono;frames++;
        }
    }
    static double Db(double power)=>Math.Max(-120,10*Math.Log10(Math.Max(1e-12,power)));
    internal string Summary()=>string.Format(CultureInfo.InvariantCulture,
        "input={0:F1} dBFS, mono={1:F1} dBFS, peak={2:F3}, frames={3}",RawDb,MonoDb,peak,frames);
    internal void Reset(){rawSquares=monoSquares=0;samples=frames=0;peak=0;}
}
