using System.Buffers.Binary;
using System.Text;



namespace Gp200.Replica;

public sealed record SnapToneSlot(int Index,ushort State,string Name)
{
    public override string ToString()=>$"{(Index<5?"AMP":"DST")} {Index%5+1} · {Name}";
}

/// <summary>Read the installed SnapTone catalog used by the editor and loader.</summary>
public static class SnapToneSlots
{
    public static IReadOnlyList<SnapToneSlot> Catalog(IEnumerable<WireRecord> records)
    {
        var result=new Dictionary<int,SnapToneSlot>();
        foreach(var r in records.Where(r=>r.Type==0x1012))
        {
            if(r.Value.Length!=24)throw new InvalidDataException(UiText.T("Registro SnapTone de longitud desconocida."));
            int slot=BinaryPrimitives.ReadUInt16LittleEndian(r.Value);
            if(slot>=10||result.ContainsKey(slot))throw new InvalidDataException(UiText.T("Slot SnapTone fuera de banco o duplicado."));
            result.Add(slot,new(slot,BinaryPrimitives.ReadUInt16LittleEndian(r.Value.AsSpan(2)),Encoding.Latin1.GetString(r.Value,8,16).Split('\0')[0]));
        }
        return result.Values.OrderBy(s=>s.Index).ToArray();
    }
}

