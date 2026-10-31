using System.Text.Json;

using System.Security.Cryptography;

namespace Gp200.Replica;

internal interface IMx42NamTransport

{

    bool Available {get;}int Chunk {get;}
    bool RecoveryGuard=>true;

    Task<PresetDocument> Active();Task<Mx42Reply> Request(byte op,uint revision=0,uint arg0=0,uint arg1=0,byte[]? payload=null);Task RefreshNames();

}

internal sealed class Mx42NamDeviceTransport:IMx42NamTransport

{

    public bool RecoveryGuard=>Mx42Integration.Supports(Mx42Capability.NamRecovery);
    public bool Available=>Mx42Integration.Supports(Mx42Capability.NamUpload)&&Mx42Integration.MaxChunk==1024;public int Chunk=>Mx42Integration.MaxChunk;

    public Task<PresetDocument> Active()=>new BankService().ActiveAsync();

    public Task<Mx42Reply> Request(byte op,uint revision=0,uint arg0=0,uint arg1=0,byte[]? payload=null)=>Mx42Integration.Request(5,op,revision,arg0,arg1,payload);

    public Task RefreshNames()=>Mx42Integration.ReadNamNames(CatalogRuntime.Current);

}

internal sealed record Mx42NamSlot(int Index,string Name,uint Revision,uint Flags,bool Verified=true)

{
    internal bool CanLoad=>Verified&&Flags==0;
    internal string Reason=>!Verified?"status not read":(Flags&1)!=0?"selected in preset":(Flags&2)!=0?"still loaded":(Flags&4)!=0?"GP-200 busy":"available";
    public override string ToString()=>$"NAM {Index+1:00} · {(Name.Length==0?"empty":Name)} · {Reason}";
}

internal static class Mx42NamService

{

    internal static string RecoveryRoot=>Path.Combine(AppPaths.Backups,"NAM");

    internal static void Check(Mx42Reply reply)

    {

        if(reply.Status==0)return;string reason=reply.Status switch{1=>"runtime not ready",2=>"invalid request",3=>"slot changed or stale session",4=>"slot selected, loaded or transition busy",5=>"invalid A2 Lite model",6=>"storage failed or result uncertain",7=>"storage recovery blocked",_=>"unknown status"};throw new IOException($"NAM USB rejected request: {reason} ({reply.Status}). No automatic retry.");

    }

    internal static Mx42NamSlot Info(Mx42Reply reply,int slot)

    {Check(reply);if(reply.Arg0!=Mx42NamCodec.Size||reply.Arg1>7)throw new InvalidDataException("Invalid NAM INFO size/flags.");return new(slot,Mx42NamCodec.Header(reply.Payload,slot),reply.Revision,reply.Arg1);}

    internal static async Task<byte[]> Read(IMx42NamTransport transport,Mx42NamSlot info)

    {

        byte[] record=new byte[Mx42NamCodec.Size];for(int offset=0;offset<record.Length;offset+=transport.Chunk)

        {

            var reply=await transport.Request(5,info.Revision,(uint)info.Index,(uint)offset);Check(reply);

            if(reply.Revision!=info.Revision||reply.Arg0!=info.Index||reply.Arg1!=offset||reply.Payload.Length!=Math.Min(transport.Chunk,record.Length-offset))throw new IOException("NAM READ revision/offset differs; stopped without retry.");reply.Payload.CopyTo(record,offset);

        }

        if(Mx42NamCodec.Crc(record)!=info.Revision)throw new InvalidDataException("NAM READ full-record CRC mismatch.");Mx42NamCodec.Validate(record,info.Index,true);return record;

    }

    internal static async Task<IReadOnlyList<Mx42NamSlot>> Slots()

    {

        var transport=new Mx42NamDeviceTransport();if(!transport.Available)throw new NotSupportedException("This firmware has no direct NAM32 USB loader. Existing inventory is read-only; NH04 is not used.");

        await DeviceMidi.OperationGate.WaitAsync();try{var slots=new List<Mx42NamSlot>();for(int i=0;i<32;i++)slots.Add(Info(await transport.Request(0,arg0:(uint)i),i));return slots;}finally{DeviceMidi.OperationGate.Release();}

    }

    internal static async Task Run(IMx42NamTransport transport,byte[] record,int slot,string directory,IProgress<string>? progress=null)

    {

        Mx42NamCodec.Validate(record,slot);if(!transport.RecoveryGuard)throw new NotSupportedException("Install the matching NAM boot-recovery firmware extension before loading. No data was sent.");if(!transport.Available||transport.Chunk is <1 or >1024)throw new NotSupportedException("Direct NAM32 USB loader capability is unavailable.");

        Directory.CreateDirectory(directory);string stage="preflight";int commitAttempts=0;

        void State(string? error=null,bool required=false)
        {
            var entry=new{utc=DateTime.UtcNow,stage,slot,commitAttempts,error,automaticRetry=false,automaticRollback=false};
            string json=JsonSerializer.Serialize(entry,new JsonSerializerOptions{WriteIndented=true});
            if(error==null&&(commitAttempts==0||required))
            {
                File.WriteAllText(Path.Combine(directory,"state.json"),json);
                File.AppendAllText(Path.Combine(directory,"stages.jsonl"),JsonSerializer.Serialize(entry)+Environment.NewLine);
            }
            else EditorAuditLog.Supplement(Path.Combine(directory,"state.json"),entry);
        }

        void Unused(PresetDocument p){if(Enumerable.Range(0,11).Any(m=>p.GetEffect(m).Code==(0xea0b0000u|(uint)slot)))throw new InvalidOperationException("The destination NAM is selected in the current preset, including a bypassed block. Select another model before loading.");}

        try

        {

            State();var before=await transport.Active();Unused(before);File.WriteAllBytes(Path.Combine(directory,"active-before.prst"),before.ToBytes());

            var info=Info(await transport.Request(0,arg0:(uint)slot),slot);if(info.Flags!=0)throw new InvalidOperationException("Destination NAM is selected, loaded or transitioning; no upload started.");

            var previous=await Read(transport,info);File.WriteAllBytes(Path.Combine(directory,"previous-record.bin"),previous);File.WriteAllText(Path.Combine(directory,"previous-info.json"),JsonSerializer.Serialize(new{info,sha256=Convert.ToHexString(SHA256.HashData(previous))}));

            var checkedPreset=await transport.Active();Unused(checkedPreset);if(before.Index!=checkedPreset.Index||!PresetComparison.EquivalentFunctional(before,checkedPreset,CatalogRuntime.Current))throw new IOException("Active preset changed during NAM preflight. No upload started.");

            var begin=await transport.Request(1,info.Revision,(uint)slot,Mx42NamCodec.Size);Check(begin);

            if(begin.Revision!=info.Revision||begin.Arg0!=Mx42NamCodec.Size||begin.Arg1==0||begin.Payload.Length!=0)throw new IOException("Invalid NAM BEGIN confirmation.");uint token=begin.Arg1;File.WriteAllText(Path.Combine(directory,"begin-ack.json"),JsonSerializer.Serialize(begin));

            stage="staging";State();

            for(int offset=0;offset<record.Length;offset+=transport.Chunk)

            {

                var chunk=record.AsSpan(offset,Math.Min(transport.Chunk,record.Length-offset)).ToArray();var ack=await transport.Request(2,info.Revision,(uint)offset,token,chunk);Check(ack);

                if(ack.Revision!=info.Revision||ack.Arg0!=offset+chunk.Length||ack.Arg1!=token||ack.Payload.Length!=0)throw new IOException("NAM CHUNK cursor/session differs; upload was not repeated.");File.WriteAllText(Path.Combine(directory,$"chunk-{offset:0000}-ack.json"),JsonSerializer.Serialize(ack));progress?.Report($"NAM {offset+chunk.Length}/{record.Length}");

            }

            stage="commit-sent-or-uncertain";commitAttempts=1;State(required:true);progress?.Report("Writing NAM and verifying storage…");var committed=await transport.Request(3,info.Revision,(uint)slot,token);Check(committed);EditorAuditLog.Supplement(Path.Combine(directory,"commit-ack.json"),committed);

            uint crc=Mx42NamCodec.Crc(record);if(committed.Arg0!=Mx42NamCodec.Size||committed.Arg1!=slot||committed.Revision!=crc||!committed.Payload.SequenceEqual(record.AsSpan(0,64).ToArray()))throw new IOException("NAM COMMIT did not confirm the exact record. Do not repeat the upload.");

            stage="physical-readback";State();progress?.Report("Reading back the complete NAM record…");var actual=await Read(transport,new(slot,Mx42NamCodec.Header(committed.Payload,slot),crc,0));EditorAuditLog.SupplementBytes(Path.Combine(directory,"readback-record.bin"),actual);

            if(!actual.SequenceEqual(record))throw new IOException("NAM physical readback differs. Do not repeat the upload.");

            stage="readback-verified";State();progress?.Report("Refreshing installed NAM names…");await transport.RefreshNames();stage="complete";State();

        }

        catch(Exception error){State(error.ToString());if(stage=="readback-verified")throw new IOException("NAM full physical readback was VERIFIED, but names could not be refreshed. Do not upload again. Logs: "+directory,error);throw;}

    }

    internal static async Task<string> Upload(string path,int slot,string name,IProgress<string>? progress=null)

    {

        byte[] weights=A2LiteModel.ReadWeights(path);byte[] record=Mx42NamCodec.Create(slot,name,weights);

        string directory=Path.Combine(RecoveryRoot,$"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory,"request.json"),JsonSerializer.Serialize(new{schema="gp200-mx42-native-nam-job/v1",slot,name,recordSha256=Convert.ToHexString(SHA256.HashData(record)),weightsSha256=Convert.ToHexString(SHA256.HashData(weights))},new JsonSerializerOptions{WriteIndented=true}));

        await DeviceMidi.OperationGate.WaitAsync();try{await Run(new Mx42NamDeviceTransport(),record,slot,directory,progress);}finally{DeviceMidi.OperationGate.Release();}return directory;

    }

}

