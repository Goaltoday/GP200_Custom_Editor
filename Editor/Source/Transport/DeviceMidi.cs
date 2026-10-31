using System.Buffers.Binary;
namespace Gp200.Replica;

/// <summary>Normal editor protocol only; firmware and factory reset have no route here.</summary>
public static class DeviceMidi
{
    public static readonly SemaphoreSlim OperationGate=new(1,1);
    internal static Task<DeviceResponse> ExchangeAsync(byte command,IReadOnlyList<WireRecord> records,Func<IReadOnlyList<WireRecord>,bool>? responseMatch=null,int timeoutMs=3000)
        =>DeviceSession.Current.Exchange(command,records,responseMatch,timeoutMs);
    internal static Task<DeviceResponse> ExchangeHardwareAsync(byte command,IReadOnlyList<WireRecord> records,Func<IReadOnlyList<WireRecord>,bool>? responseMatch=null,int timeoutMs=3000)
    {
        if(command is not (0x11 or 0x12))throw new InvalidOperationException(UiText.T("Comando fuera del protocolo de edición."));
        if(records.Count==0||timeoutMs is <50 or >15000)throw new ArgumentException(UiText.T("Operación MIDI inválida."));
        if(command==0x12)foreach(var r in records)ValidateWrite(r);
        return command==0x12?AuditedWrite(records,responseMatch,timeoutMs):ReadOnlyMidi.ExchangeInternalAsync(command,records,responseMatch,timeoutMs);
    }
    static async Task<DeviceResponse> AuditedWrite(IReadOnlyList<WireRecord> records,Func<IReadOnlyList<WireRecord>,bool>? match,int timeoutMs)
    {
        string path=await EditorAuditLog.PrepareAsync("tx",new{utc=DateTime.UtcNow,state="prepared",records});
        try
        {
            var response=await ReadOnlyMidi.ExchangeInternalAsync(0x12,records,match,timeoutMs);
            await EditorAuditLog.CompleteAsync(path,new{utc=DateTime.UtcNow,state="driver-send-complete-not-readback",records,response});return response;
        }
        catch(Exception error){await EditorAuditLog.CompleteAsync(path,new{utc=DateTime.UtcNow,state="failed-or-uncertain",records,error=error.Message});throw;}
    }
    static void ValidateWrite(WireRecord r)
    {
        bool valid=r.Type switch
        {
            0=>r.Value.Length==4&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)<=1,1=>r.Value.Length==4,
            2=>r.Value.Length==88,5=>r.Value.Length==12,6=>r.Value.Length==4,
            7 or 9=>r.Value.Length==20,8=>r.Value.Length==16,10=>r.Value.Length==20,11=>r.Value.Length==44,
            12=>r.Value.Length==12,13=>r.Value.Length==4,14=>r.Value.Length==8,15=>r.Value.Length==8,16=>r.Value.Length==4,
            19=>r.Value.Length==4,20=>r.Value.Length==68,21=>r.Value.Length==4,22=>r.Value.Length==8,
            0x1001 or 0x1008 or 0x100f or 0x1011=>r.Value.Length==4,
            0x1005=>r.Value.Length==8,0x100a=>r.Value.Length==4120,0x100c or 0x1015=>r.Value.Length==20,
            0x1013=>r.Value.Length==0x2018&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)<10,
            0x2003=>r.Value.Length==20,0x2009=>r.Value.Length==4,
            _=>false
        };
        if(!valid)throw new InvalidDataException(UiText.T($"Tipo de escritura no admitido: {r.Type:X4}/{r.Value.Length}."));
        if(r.Type==0x1008&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value) is 37 or 38)throw new InvalidOperationException(UiText.T("Operación de sistema excluida."));
        if(r.Type==0x1008&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==39&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value.AsSpan(2))>2)throw new InvalidOperationException(UiText.T("Plantilla fuera de rango."));
        if(r.Type==0x1008&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==40&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value.AsSpan(2))>2)throw new InvalidOperationException(UiText.T("Modo de pantalla fuera de rango."));
    }
    public static Task<DeviceResponse> SendSettingsAsync(WireRecord record)
    {
        if(record.Type is not (0x1008 or 0x1005 or 0x100f or 0x1001 or 0x1011))throw new ArgumentException(UiText.T("Tipo de ajuste inválido."));
        return ExchangeAsync(0x12,[record]);
    }
    public static Task<DeviceResponse> SendIrAsync(WireRecord record,Func<IReadOnlyList<WireRecord>,bool>? match=null)
    {
        if(record.Type is not (0x100a or 0x100c or 0x1015 or 0x1008))throw new ArgumentException(UiText.T("Tipo IR inválido."));
        return ExchangeAsync(0x12,[record],match,10000);
    }
    public static async Task<IReadOnlyList<WireRecord>> ReadCatalogAsync(ushort type)
    {
        int count=type switch{0x1009=>20,0x1012=>10,_=>throw new ArgumentOutOfRangeException(nameof(type))};
        await OperationGate.WaitAsync();try
        {
            var result=new List<WireRecord>();
            for(int slot=0;slot<count;slot++)
            {
                byte[] query=new byte[24];BinaryPrimitives.WriteUInt16LittleEndian(query,(ushort)slot);query[2]=1;
                int requested=slot;var response=await ExchangeAsync(0x11,[new(type,query)],records=>records.Any(r=>r.Type==type&&r.Value.Length==24&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==requested));
                if(response.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver no liberó los puertos MIDI."));
                result.Add(response.Records.Single(r=>r.Type==type&&r.Value.Length==24&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==slot));
            }
            return result;
        }finally{OperationGate.Release();}
    }
}
