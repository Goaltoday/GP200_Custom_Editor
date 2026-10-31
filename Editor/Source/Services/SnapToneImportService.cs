using System.Text.Json;
namespace Gp200.Replica;
public static class SnapToneImportService
{
    // Firmware 1.8.2 returns at most 15 printable characters plus NUL in
    // the 16-byte catalog field, even when the import contains 16 characters.
    internal static bool CatalogConfirms(SnapToneSlot actual,int slot,string requested)
        =>actual.Index==slot&&actual.State==0&&(actual.Name.TrimEnd()==requested.TrimEnd()
            ||(requested.Length==16&&actual.Name.TrimEnd()==requested[..15].TrimEnd()));
    internal static bool IsImportResponse(WireRecord record,int slot)=>record.Type==0x1014&&record.Value.Length==8&&System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.Value)==slot;
    public static async Task LoadAsync(SnapToneJob job,int slot,string name,IProgress<string>? progress=null)
    {
        if(job.Clo==null)throw new InvalidOperationException("Convert or choose a CLO first.");
        var clo=new CloDocument(File.ReadAllBytes(job.Clo));var record=clo.UploadRecord(slot,name);
        await DeviceMidi.OperationGate.WaitAsync();
        try
        {
            string attempt=Path.Combine(job.Directory,"imports",$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");Directory.CreateDirectory(attempt);
            var before=await ReadCatalogUnlocked();File.WriteAllText(Path.Combine(attempt,"catalog-before.json"),JsonSerializer.Serialize(before));
            job.State="uploading";job.Save();progress?.Report(UiText.T("Uploading SnapTone…"));DeviceResponse? response=null;string? responseError=null;
            try{response=await DeviceMidi.ExchangeAsync(0x12,[record],records=>records.Any(r=>IsImportResponse(r,slot)),8000);}
            catch(IOException error){responseError=error.Message;}
            EditorAuditLog.Supplement(Path.Combine(attempt,"upload-response.json"),new{utc=DateTime.UtcNow,slot,name,requested=record,response,responseError,policy="No retry; catalog verification is not binary model readback."});
            if(response?.CleanupErrors.Count>0)throw new IOException("MIDI ports were not released correctly.");
            var ack=response?.Records.SingleOrDefault(r=>IsImportResponse(r,slot));
            if(ack==null||System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(ack.Value.AsSpan(2))!=1)throw new IOException("SnapTone upload was not positively confirmed. It was not repeated. "+responseError);
            progress?.Report(UiText.T("Verifying SnapTone catalog…"));IReadOnlyList<SnapToneSlot> after=[];
            for(int readAttempt=0;readAttempt<8;readAttempt++)
            {
                after=await ReadCatalogUnlocked();var actual=after.Single(s=>s.Index==slot);
                EditorAuditLog.Supplement(Path.Combine(attempt,$"catalog-read-{readAttempt+1}.json"),after);
                if(CatalogConfirms(actual,slot,name))
                {
                    if(before.Where(s=>s.Index!=slot).Any(s=>!after.Contains(s)))throw new IOException("Another SnapTone catalog slot changed unexpectedly.");
                    EditorAuditLog.Supplement(Path.Combine(attempt,"catalog-after.json"),after);job.State="catalog-confirmed";job.SaveSupplemental();return;
                }
                await Task.Delay(250);
            }
            throw new IOException("SnapTone catalog did not confirm the requested name. Upload was not repeated; inspect the device before retrying.");
        }
        catch(Exception error){job.State="upload-uncertain";job.SaveSupplemental(error:error.Message);throw;}
        finally{DeviceMidi.OperationGate.Release();}
    }
    // OperationGate belongs to the caller; DeviceMidi.ReadCatalogAsync owns it
    // itself and therefore cannot be called from inside this transaction.
    static async Task<IReadOnlyList<SnapToneSlot>> ReadCatalogUnlocked()
    {
        var records=new List<WireRecord>();for(int slot=0;slot<10;slot++)
        {
            byte[] query=new byte[24];query[0]=(byte)slot;query[2]=1;int requested=slot;
            var response=await DeviceMidi.ExchangeAsync(0x11,[new(0x1012,query)],r=>r.Any(x=>x.Type==0x1012&&x.Value.Length==24&&x.Value[0]==requested));
            if(response.CleanupErrors.Count>0)throw new IOException("MIDI ports were not released correctly.");records.AddRange(response.Records.Where(r=>r.Type==0x1012));
        }
        return SnapToneSlots.Catalog(records);
    }
}
