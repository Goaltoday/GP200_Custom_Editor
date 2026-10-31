using System.Buffers.Binary;
using System.Text.Json;
namespace Gp200.Replica;

// Research transport for the reviewed firmware. No firmware access and
// no automatic replay. Commands 0..10 are the existing Scene Remote mailbox.
internal static class SceneMailbox
{
    internal static async Task<int> Command(int slot,uint volumeCode,int command,string? evidence,bool operationLocked=false,PresetDocument? knownActive=null)
    {
        if(slot is <0 or >255||command is <0 or >10)throw new ArgumentOutOfRangeException();
        if(knownActive!=null&&!operationLocked)throw new InvalidOperationException("Known active context requires the MIDI operation lock.");
        var current=knownActive??await new BankService().ActiveAsync();if(current.Index!=slot)throw new IOException("Active slot changed before Scene command.");
        byte[] value=new byte[12];value[0]=10;value[1]=15;BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4),volumeCode);BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(8),0x53430000u|((uint)slot<<8)|(uint)command);
        bool Match(WireRecord r)=>r.Type==5&&r.Value.Length==12&&r.Value[0]==10&&r.Value[1]==15&&r.Value[9]==slot&&r.Value[11]==0x53&&r.Value[10] is 0x43 or 0x52&&r.Value[8]<=5;
        if(!operationLocked)await DeviceMidi.OperationGate.WaitAsync();
        try
        {
            var response=await DeviceMidi.ExchangeAsync(0x12,[new(0,new byte[4]),new(5,value)],rs=>rs.Any(Match),3000);
            if(evidence!=null)EditorAuditLog.Supplement(evidence,new{slot,command,response});
            if(response.CleanupErrors.Count!=0)throw new IOException("Scene response cleanup failed.");
            return response.Records.Last(Match).Value[8];
        }
        finally{if(!operationLocked)DeviceMidi.OperationGate.Release();}
    }
}
