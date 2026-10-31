using System.Buffers.Binary;

namespace Gp200.Replica;

public enum ReadQuery { Identity, State, ActivePreset, SavedPreset }
public sealed record Fragment(byte Command,int Total,int Offset,byte[] Payload);
public sealed record WireRecord(ushort Type,byte[] Value);

public static class Protocol
{
    static readonly byte[] Magic=[0x21,0x25,0x7e,0x47,0x50,0x2d,0x32];
    public static IReadOnlyList<byte[]> Encode(byte command,IReadOnlyList<WireRecord> records)
    {
        using var stream=new MemoryStream();byte[] header=new byte[4];
        foreach(var record in records){if(record.Value.Length>ushort.MaxValue)throw new InvalidDataException(UiText.T("Registro demasiado largo."));BinaryPrimitives.WriteUInt16LittleEndian(header,record.Type);BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2),(ushort)record.Value.Length);stream.Write(header);stream.Write(record.Value);}
        byte[] payload=stream.ToArray();if(payload.Length is <1 or >16383)throw new InvalidDataException(UiText.T("Longitud de mensaje inválida."));
        var frames=new List<byte[]>();
        for(int offset=0;offset<payload.Length;offset+=183)
        {
            int count=Math.Min(183,payload.Length-offset);var frame=new List<byte>{0xf0};frame.AddRange(Magic);
            frame.AddRange([command,(byte)(payload.Length&127),(byte)(payload.Length>>7),(byte)(offset&127),(byte)(offset>>7)]);
            foreach(byte value in payload.AsSpan(offset,count)){frame.Add((byte)(value>>4));frame.Add((byte)(value&15));}frame.Add(0xf7);frames.Add(frame.ToArray());
        }
        return frames;
    }
    public static byte[] ReadRequest(ReadQuery query,int slot=0)
    {
        if(slot is <0 or >255)throw new ArgumentOutOfRangeException(nameof(slot));
        byte[] payload;
        if(query is ReadQuery.Identity or ReadQuery.State)
        {
            payload=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(payload,query==ReadQuery.Identity?(ushort)0x2001:(ushort)0x1006);
        }
        else if(query is ReadQuery.ActivePreset or ReadQuery.SavedPreset)
        {
            payload=new byte[16];
            Put(2,4);Put(4,query==ReadQuery.SavedPreset?(ushort)1:(ushort)0);Put(6,(ushort)slot);
            Put(8,1);Put(10,4);Put(12,query==ReadQuery.SavedPreset?(ushort)slot:ushort.MaxValue);Put(14,(ushort)slot);
            void Put(int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(at,2),value);
        }
        else throw new ArgumentOutOfRangeException(nameof(query));
        var frame=new List<byte>{0xf0};frame.AddRange(Magic);frame.AddRange([0x11,(byte)payload.Length,0,0,0]);
        foreach(byte b in payload){frame.Add((byte)(b>>4));frame.Add((byte)(b&15));}frame.Add(0xf7);return frame.ToArray();
    }
    public static Fragment Decode(byte[] frame)
    {
        if(frame.Length<14||frame[0]!=0xf0||frame[^1]!=0xf7||!frame.AsSpan(1,7).SequenceEqual(Magic))throw new InvalidDataException(UiText.T("Trama SysEx no reconocida."));
        if(frame.AsSpan(8,5).ContainsAnyInRange((byte)128,byte.MaxValue))throw new InvalidDataException(UiText.T("Cabecera fuera de rango MIDI."));
        if((frame.Length-14)%2!=0)throw new InvalidDataException(UiText.T("Datos nibble truncados."));
        var payload=new byte[(frame.Length-14)/2];
        for(int i=0;i<payload.Length;i++){byte hi=frame[13+2*i],lo=frame[14+2*i];if(hi>15||lo>15)throw new InvalidDataException(UiText.T("Datos nibble inválidos."));payload[i]=(byte)(hi*16+lo);}
        int total=frame[9]+128*frame[10],offset=frame[11]+128*frame[12];
        if(offset+payload.Length>total)throw new InvalidDataException(UiText.T("Fragmento fuera del mensaje."));
        return new(frame[8],total,offset,payload);
    }
    public static byte[] Reassemble(IReadOnlyList<byte[]> frames)
    {
        if(frames.Count==0)throw new InvalidDataException(UiText.T("No se recibió respuesta."));
        var first=Decode(frames[0]);using var output=new MemoryStream();
        foreach(byte[] frame in frames){var part=Decode(frame);if(part.Command!=first.Command||part.Total!=first.Total||part.Offset!=output.Length)throw new InvalidDataException(UiText.T("Respuesta incompleta, duplicada o fuera de orden."));output.Write(part.Payload);}
        if(output.Length!=first.Total)throw new InvalidDataException(UiText.T("Respuesta truncada."));return output.ToArray();
    }
    public static List<WireRecord> Records(byte[] payload)
    {
        var list=new List<WireRecord>();int at=0;
        while(at<payload.Length){if(payload.Length-at<4)throw new InvalidDataException(UiText.T("Cabecera TLV truncada."));ushort type=BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(at,2));int size=BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(at+2,2));at+=4;if(size>payload.Length-at)throw new InvalidDataException(UiText.T("TLV truncado."));list.Add(new(type,payload.AsSpan(at,size).ToArray()));at+=size;}return list;
    }
}
