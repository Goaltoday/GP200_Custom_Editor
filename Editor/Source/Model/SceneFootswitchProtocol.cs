using System.Buffers.Binary;
using System.Text.Json;
namespace Gp200.Replica;
internal sealed record SceneFsTable(byte[] Actions,uint Revision,int Status);
internal static class SceneFootswitchProtocol
{
    internal static uint Crc(ReadOnlySpan<byte> bytes)=>Crc32.Compute(bytes);
    internal static WireRecord Request(uint nonce,int op=0,int slot=0,int action=0,uint revision=0)
    {
        if(nonce==0||op is <0 or >1||slot is <0 or >15||action is <0 or >31||op==0&&(slot!=0||action!=0||revision!=0))throw new ArgumentOutOfRangeException();
        byte[] b=new byte[32];BinaryPrimitives.WriteUInt16LittleEndian(b,0x5346);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2),(ushort)op);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4),0x31554653);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8),nonce);b[12]=1;b[13]=(byte)slot;b[14]=(byte)action;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16),revision);return new(0x1009,b);
    }
    internal static SceneFsTable? Decode(WireRecord r,uint nonce,int op)
    {
        var b=r.Value;if(r.Type!=0x1009||b.Length!=40||BinaryPrimitives.ReadUInt16LittleEndian(b)!=0x5346||BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4))!=0x31554653||BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8))!=nonce||BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2))!=op)return null;
        if(b[12]!=1||b[13]>3||b[14]!=16||b[15]!=31||b.AsSpan(36,3).ContainsAnyExcept((byte)0)||((0x09+0x10+40+b.Sum(x=>(int)x))&255)!=0)throw new InvalidDataException("SFU1 response integrity failed.");
        var actions=b[20..36];uint revision=BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(16));if(b[13]!=1&&Crc(actions)!=revision)throw new InvalidDataException("SFU1 table CRC differs.");return new(actions,revision,b[13]);
    }
    static async Task<SceneFsTable> Exchange(int op=0,int slot=0,int action=0,uint revision=0)
    {
        uint nonce=(uint)Random.Shared.Next(1,int.MaxValue);var response=await DeviceMidi.ExchangeAsync(0x11,[Request(nonce,op,slot,action,revision)],records=>records.Any(r=>Decode(r,nonce,op)!=null),1500);
        if(response.CleanupErrors.Count>0)throw new IOException("MIDI cleanup failed.");return response.Records.Select(r=>Decode(r,nonce,op)).FirstOrDefault(r=>r!=null)??throw new IOException("SFU1 not supported or no response.");
    }
    internal static async Task<SceneFsTable> ReadAsync()
    {
        await DeviceMidi.OperationGate.WaitAsync();try{return await Exchange();}finally{DeviceMidi.OperationGate.Release();}
    }
    internal static async Task<SceneFsTable> SetAsync(SceneFsTable before,int slot,int action)
    {
        if(before.Status!=0)throw new InvalidOperationException("Read SFU1 before editing.");
        _=Request(1,1,slot,action,before.Revision);await DeviceMidi.OperationGate.WaitAsync();try
        {
            var current=await Exchange();if(current.Status!=0||current.Revision!=before.Revision||!current.Actions.SequenceEqual(before.Actions))throw new IOException("Footswitch assignments changed on the GP-200. Read again.");
            string dir=Path.Combine(AppPaths.Backups,$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-scene-fs-{Guid.NewGuid():N}");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"before.json"),JsonSerializer.Serialize(new{protocol="SFU1",table=before,slot,action}));
            // One send only: after a timeout, the user must read again; never retry a SET automatically.
            var ack=await Exchange(1,slot,action,before.Revision);EditorAuditLog.Supplement(Path.Combine(dir,"ack.json"),ack);
            if(ack.Status!=0)throw new IOException(ack.Status==3?"SFU1 conflict. Read again.":"SFU1 assignment rejected.");
            var after=await Exchange();var expected=before.Actions.ToArray();expected[slot]=(byte)action;
            if(after.Status!=0||!after.Actions.SequenceEqual(expected))throw new IOException("SFU1 readback differs. No upload repeated; read again.");
            EditorAuditLog.Supplement(Path.Combine(dir,"verified.json"),after);return after;
        }finally{DeviceMidi.OperationGate.Release();}
    }
}
