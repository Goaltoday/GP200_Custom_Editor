using System.Buffers.Binary;
namespace Gp200.Replica;
internal sealed record Mx42Reply(ushort Status,uint Revision,uint Arg0,uint Arg1,byte[] Payload);
internal static class Mx42Integration
{
    const uint Magic=MxeLayout.Magic;
    internal static int MaxChunk {get=>DeviceSession.Current.MaxChunk;private set=>DeviceSession.Current.MaxChunk=value;}
    internal static uint Capabilities=>DeviceSession.Current.Capabilities;
    internal static bool Supports(Mx42Capability flag)=>Supports((uint)flag);
    internal static bool Supports(uint flag)=>CatalogRuntime.Current.IsMx42&&(Capabilities&flag)==flag;
    internal static void Reset(){DeviceSession.Current.Reset();}
    internal static async Task<bool> Probe()
    {
        Reset();if(!CatalogRuntime.Current.IsMx42)return false;
        CatalogRuntime.Current.InvalidateMx42DeviceNames();CatalogRuntime.Current.ClearDeviceCabMap();
        try
        {
            var r=await Request(0,0);var p=r.Payload;
            if(r.Status!=0||p.Length!=MxeCapabilitiesLayout.Size||U(p,MxeCapabilitiesLayout.Version)!=MxeLayout.Version||U(p,MxeCapabilitiesLayout.NamSlots)!=32||U(p,MxeCapabilitiesLayout.Footswitches)!=8||U(p,MxeCapabilitiesLayout.SceneBytes)!=SceneLayout.Size||U(p,MxeCapabilitiesLayout.Chunk) is not (128 or 1024)||U(p,MxeCapabilitiesLayout.Families)!=12||U(p,MxeCapabilitiesLayout.LayoutId)!=MxeCapabilitiesLayout.ExpectedLayoutId)return false;
            DeviceSession.Current.Capabilities=U(p,MxeCapabilitiesLayout.Flags);MaxChunk=(int)U(p,MxeCapabilitiesLayout.Chunk);return true;
        }
        catch(IOException){return false;}
    }
    static uint U(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    internal static async Task<Mx42Reply> Request(byte service,byte op,uint revision=0,uint arg0=0,uint arg1=0,byte[]? payload=null)
    {
        payload??=[];if(payload.Length>MaxChunk)throw new ArgumentOutOfRangeException(nameof(payload));
        uint nonce;do{nonce=BitConverter.ToUInt32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));}while(nonce==0);
        byte[] value=new byte[MxeLayout.Header+payload.Length];BinaryPrimitives.WriteUInt16LittleEndian(value,MxeLayout.Marker);value[MxeLayout.Service]=service;value[MxeLayout.Operation]=op;
        void Put(int at,uint x)=>BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(at),x);
        Put(MxeLayout.MagicAt,Magic);Put(MxeLayout.Nonce,nonce);BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(MxeLayout.VersionAt),MxeLayout.Version);Put(MxeLayout.Revision,revision);Put(MxeLayout.Arg0,arg0);Put(MxeLayout.Arg1,arg1);Put(MxeLayout.Length,(uint)payload.Length);payload.CopyTo(value,MxeLayout.Header);
        bool Matches(WireRecord r)=>r.Type==MxeLayout.RecordType&&r.Value.Length>=MxeLayout.Header&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==MxeLayout.Marker&&r.Value[MxeLayout.Service]==service&&r.Value[MxeLayout.Operation]==op&&U(r.Value,MxeLayout.MagicAt)==Magic&&U(r.Value,MxeLayout.Nonce)==nonce;
        // NAM COMMIT includes bounded erase/program/readback of journal and target sectors.
        // Allow its one reply to complete; never retry a timed-out commit.
        int timeout=service==5&&op==3?15000:service==0?500:3000;
        var kind=DeviceOperations.Integration(service,op);
        string? audit=kind==DeviceOperationKind.Query?null:await EditorAuditLog.PrepareAsync("integration",new{service,op,kind=kind.ToString(),revision,arg0,arg1,payload,nonce,state="prepared"});
        DeviceResponse response;
        try{response=await DeviceSession.Current.Exchange(0x11,[new(MxeLayout.RecordType,value)],rs=>rs.Any(Matches),timeout);}
        catch(Exception error){if(audit!=null)await EditorAuditLog.CompleteAsync(audit,new{service,op,kind=kind.ToString(),state="failed-or-uncertain",error=error.Message});throw;}
        if(audit!=null)await EditorAuditLog.CompleteAsync(audit,new{service,op,kind=kind.ToString(),state="response-received-not-readback",response});
        if(response.CleanupErrors.Count!=0)throw new IOException("MIDI driver did not release the integration query.");
        var b=response.Records.Single(Matches).Value;
        if(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(MxeLayout.VersionAt))!=MxeLayout.Version||U(b,MxeLayout.Length)>MaxChunk||b.Length!=MxeLayout.Header+U(b,MxeLayout.Length))throw new InvalidDataException("Invalid MXE1 reply size/version.");
        return new(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(MxeLayout.Status)),U(b,MxeLayout.Revision),U(b,MxeLayout.Arg0),U(b,MxeLayout.Arg1),b[MxeLayout.Header..]);
    }
    internal static async Task WaitForIdle()
    {
        if(!Supports(Mx42Capability.SceneSnapshot))return;
        var clock=System.Diagnostics.Stopwatch.StartNew();
        do
        {
            var ready=await Request(2,0);
            if(ready.Status==0)return;
            if(ready.Status is not (1 or 3))throw new IOException("MX42 readiness rejected (status "+ready.Status+"). No write was repeated.");
            await Task.Delay(5);
        }while(clock.ElapsedMilliseconds<3000);
        throw new IOException("MX42 did not become ready. No write was repeated.");
    }
    internal static async Task ValidatePresetPlan(PresetDocument document)
    {
        Mx42NamLimit.Validate(document);
        if(!Supports(Mx42Capability.PresetAdmission))throw new NotSupportedException("Firmware has no reviewed full-preset resource admission service.");
        byte[] ids=new byte[44];for(int slot=0;slot<11;slot++)BinaryPrimitives.WriteUInt32LittleEndian(ids.AsSpan(slot*4),document.GetEffect(slot).Code);
        var reply=await Request(4,0,payload:ids);
        if(reply.Status!=0)throw new IOException("The target preset does not fit the firmware resource plan, or its identity is invalid. No persistent preset write was sent.");
    }
    internal static async Task ReadCabMap(Catalog catalog)
    {
        if(!catalog.IsMx42||!Supports(Mx42Capability.CabMap)||catalog.HasMx42DeviceCabMap)return;
        var reply=await Request(3,1);
        if(reply.Status!=0||reply.Revision!=0||reply.Arg1!=0||reply.Arg0 is <1 or >256||reply.Payload.Length!=reply.Arg0*4)throw new InvalidDataException("Invalid MX42 complete CAB map reply.");
        var ids=Enumerable.Range(0,(int)reply.Arg0).Select(i=>U(reply.Payload,4*i)).ToArray();catalog.ApplyMx42CabMap(ids);
    }
    internal static async Task ReadNamNames(Catalog catalog)
    {
        // Connection discovery must know CAB ordinals before decoding its first
        // active preset. Reuse the map for subsequent NAM-name-only refreshes.
        await ReadCabMap(catalog);
        if(!Supports(Mx42Capability.NamNames))return;
        var labels=new string[32];
        for(int slot=0;slot<32;slot++)
        {
            var r=await Request(3,0,arg0:(uint)slot);var b=r.Payload;
            if(r.Status!=0||b.Length!=48||U(b,0)!=slot||U(b,4)>1)throw new InvalidDataException("Invalid NAM device inventory reply.");
            string name=System.Text.Encoding.ASCII.GetString(b.AsSpan(16,32)).Split('\0')[0];
            if(U(b,4)!=0&&(U(b,12)!=0x41324c31||name.Length is <1 or >16||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not (' ' or '_' or '-'))))throw new InvalidDataException("Invalid installed NAM metadata.");
            labels[slot]=U(b,4)==0?$"NAM {slot+1:00} · empty":$"NAM {slot+1:00} · {name}";
        }
        catalog.AcceptMx42DeviceNames(labels);
    }
}
