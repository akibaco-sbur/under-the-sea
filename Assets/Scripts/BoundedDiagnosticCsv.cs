using System;
using System.IO;
using System.Text;

/// <summary>Bounded synchronous small writes; no background queues or open handles after stop.</summary>
internal sealed class BoundedDiagnosticCsv : IDisposable
{
    readonly StreamWriter writer;
    readonly int maxRows,maxBytes;
    int bytes;
    internal int Rows {get;private set;}
    internal BoundedDiagnosticCsv(string path,string header,int maxRows=6000,int maxBytes=8*1024*1024)
    {
        this.maxRows=maxRows;this.maxBytes=maxBytes;
        writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(true));
        try {writer.WriteLine(header);bytes=Encoding.UTF8.GetByteCount(header)+5;writer.Flush();}
        catch {writer.Dispose();throw;}
    }
    internal bool Write(string line)
    {
        int size=Encoding.UTF8.GetByteCount(line)+2;
        if(Rows>=maxRows || bytes+size>maxBytes)return false;
        writer.WriteLine(line);Rows++;bytes+=size;
        if(Rows%10==0)writer.Flush();return true;
    }
    internal static string Escape(string text)=>"\""+(text??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ")+"\"";
    public void Dispose()=>writer.Dispose();
}
