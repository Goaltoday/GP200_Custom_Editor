using System.Buffers.Binary;
using System.Text;
namespace Gp200.Replica;
public sealed record PresetStyle(int Index,string Name)
{
    public override string ToString()=>$"{Index+1:00} · {Name}";
}
public static class PresetStyles
{
    public static IReadOnlyList<PresetStyle> Parse(IReadOnlyList<WireRecord> records)=>records
        .Where(r=>r.Type==10&&r.Value.Length==20)
        .Select(r=>new PresetStyle(BinaryPrimitives.ReadUInt16LittleEndian(r.Value),Encoding.Latin1.GetString(r.Value,4,16).Split('\0')[0]))
        .OrderBy(r=>r.Index).ToArray();
}
