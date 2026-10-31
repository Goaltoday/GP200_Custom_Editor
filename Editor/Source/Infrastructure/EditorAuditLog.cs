using System.Text.Json;
using System.Text.Json.Serialization;
namespace Gp200.Replica;

// Keep a pre-send record, but never turn a logging failure AFTER a
// confirmed device operation into a misleading MIDI error or automatic retry.
internal static class EditorAuditLog
{
    const int MaxFiles=2048;const long MaxBytes=64L*1024*1024;
    static readonly object sync=new();
    static readonly JsonSerializerOptions options=new(){NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
    static string? prunedRoot;static int filesSincePrune;
    internal static string? LastError {get;private set;}
    internal static void NoteFailure(Exception error)=>LastError=error.Message;
    internal static string Root=>AppPaths.Evidence;
    internal static Task<string> PrepareAsync(string kind,object entry)=>Task.Run(()=>
    {
        lock(sync)
        {
            string root=Path.GetFullPath(Root);Directory.CreateDirectory(root);
            if(prunedRoot!=root||++filesSincePrune>=32){Prune(root);prunedRoot=root;filesSincePrune=0;}
            string path=Path.Combine(root,$"{kind}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
            string json=JsonSerializer.Serialize(entry,options);if(json.Length>1024*1024)throw new IOException("Audit record is too large; no command was sent.");
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(stream)){writer.Write(json);writer.Flush();}
            return path;
        }
    });
    internal static Task<bool> CompleteAsync(string path,object entry)=>Task.Run(()=>
    {
        try
        {
            lock(sync)
            {
                string root=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Audit path is outside the evidence directory.");
                string json=JsonSerializer.Serialize(entry,options);if(json.Length>1024*1024)throw new IOException("Audit completion is too large; the pre-send record was retained.");
                File.WriteAllText(path,json);
                LastError=null;return true;
            }
        }
        catch(Exception error){LastError=error.Message;return false;}
    });
    internal static void Supplement(string path,object entry)
    {
        try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(entry,options));}
        catch(Exception error){LastError=error.Message;}
    }
    internal static void SupplementBytes(string path,byte[] bytes)
    {try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllBytes(path,bytes);}catch(Exception error){LastError=error.Message;}}
    static void Prune(string root)
    {
        var files=new DirectoryInfo(root).EnumerateFiles("*.json",SearchOption.TopDirectoryOnly).OrderBy(f=>f.LastWriteTimeUtc).ToArray();
        long bytes=files.Sum(f=>f.Length);int count=files.Length;
        foreach(var f in files)
        {
            if(count<MaxFiles&&bytes<MaxBytes)break;
            // No recursion and only files returned from this exact directory.
            long size=f.Length;f.Delete();bytes-=size;count--;
        }
    }
}
