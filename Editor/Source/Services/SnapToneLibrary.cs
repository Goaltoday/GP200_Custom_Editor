using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace Gp200.Replica;
public sealed record SnapToneJob(string Directory,string Source,string? CorrectiveIr,string? ReferenceWav)
{
    public string? Clo {get;internal set;}
    public string State {get;internal set;}="archived";
    public JsonElement? Conversion {get;internal set;}
    public void Save(object? conversion=null,string? error=null)
    {
        if(conversion!=null)Conversion=JsonSerializer.SerializeToElement(conversion);
        File.WriteAllText(Path.Combine(Directory,"manifest.json"),JsonSerializer.Serialize(new{format=1,utc=DateTime.UtcNow,state=State,source=Path.GetFileName(Source),sourceSha256=Hash(Source),correctiveIr=CorrectiveIr==null?null:Path.GetFileName(CorrectiveIr),correctiveIrSha256=CorrectiveIr==null?null:Hash(CorrectiveIr),correctivePostGainDb=CorrectiveIr==null?(int?)null:-6,referenceWav=ReferenceWav==null?null:Path.GetFileName(ReferenceWav),referenceWavSha256=ReferenceWav==null?null:Hash(ReferenceWav),clo=Clo==null?null:Path.GetFileName(Clo),cloSha256=Clo==null?null:Hash(Clo),engine="NamToClo 2.10.1",toneMatch=Path.GetExtension(Source).Equals(".nam",StringComparison.OrdinalIgnoreCase),conversion=Conversion,error},new JsonSerializerOptions{WriteIndented=true}));
    }
    public void SaveSupplemental(string? error=null){try{Save(error:error);}catch(Exception failure){EditorAuditLog.NoteFailure(failure);}}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
public static class SnapToneLibrary
{
    public static string DefaultRoot=>AppPreferences.Load().SnapToneLibraryPath;
    public static SnapToneJob Archive(string root,string source,string? ir=null,string? reference=null)
    {
        string extension=Path.GetExtension(source).ToLowerInvariant();if(extension is not (".nam" or ".clo"))throw new ArgumentException("Choose a NAM or CLO file.");
        if(extension==".clo")_=new CloDocument(File.ReadAllBytes(source));
        string label=new(Path.GetFileNameWithoutExtension(source).Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).Take(40).ToArray());
        string directory=Path.Combine(Path.GetFullPath(root),$"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{label}-{Guid.NewGuid().ToString("N")[..8]}");Directory.CreateDirectory(directory);
        string Copy(string original,string prefix){string to=Path.Combine(directory,prefix+Path.GetFileName(original));File.Copy(original,to,false);return to;}
        var job=new SnapToneJob(directory,Copy(source,""),ir==null?null:Copy(ir,"corrective-"),reference==null?null:Copy(reference,"reference-"));
        if(extension==".clo")job.Clo=job.Source;job.Save();return job;
    }
    public static async Task ConvertAsync(SnapToneJob job,IProgress<string>? progress,CancellationToken cancellation)
    {
        if(Path.GetExtension(job.Source).Equals(".clo",StringComparison.OrdinalIgnoreCase))return;
        string worker=Path.Combine(AppContext.BaseDirectory,"Converter","GP200ConversionWorker.exe");if(!File.Exists(worker))throw new FileNotFoundException("The NAM conversion component is missing.",worker);
        string request=Path.Combine(job.Directory,"request.json"),result=Path.Combine(job.Directory,"conversion-result.json");
        File.WriteAllText(request,JsonSerializer.Serialize(new{inputNam=job.Source,outputDirectory=job.Directory,correctiveIr=job.CorrectiveIr,referenceWav=job.ReferenceWav,resultFile=result}));
        job.State="converting";job.Save();var start=new ProcessStartInfo(worker){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=job.Directory};start.ArgumentList.Add(request);
        using var process=new Process{StartInfo=start};
        try
        {
            process.Start();using var registration=cancellation.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
            var errors=process.StandardError.ReadToEndAsync();
            using(var log=File.CreateText(Path.Combine(job.Directory,"conversion.log")))
            {string? line;while((line=await process.StandardOutput.ReadLineAsync())!=null){await log.WriteLineAsync(line);try{using var entry=JsonDocument.Parse(line);if(entry.RootElement.TryGetProperty("message",out var message))progress?.Report(message.GetString()!);}catch(JsonException){progress?.Report(line);}}}
            await process.WaitForExitAsync();cancellation.ThrowIfCancellationRequested();string stderr=await errors;File.WriteAllText(Path.Combine(job.Directory,"stderr.log"),stderr);
            if(!File.Exists(result))throw new IOException("Conversion failed: "+stderr);
            using var json=JsonDocument.Parse(File.ReadAllText(result));var output=json.RootElement;
            if(process.ExitCode!=0||!output.GetProperty("ok").GetBoolean())throw new IOException(output.GetProperty("error").GetString()??"NAM conversion failed.");
            string clo=Path.GetFullPath(output.GetProperty("outputClo").GetString()!);
            if(!clo.StartsWith(Path.GetFullPath(job.Directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Converter output escaped the library job.");
            _=new CloDocument(File.ReadAllBytes(clo));job.Clo=clo;job.State="converted";job.Save(output.Clone());
        }
        catch(Exception error){job.State=cancellation.IsCancellationRequested?"cancelled":"failed";job.Save(error:error.Message);throw;}
    }
}
