using System.Buffers.Binary;
namespace Gp200.Replica;
internal static class Mx42ResourcePolicy
{
    static bool Label(ReadOnlySpan<byte> b)
    {
        int end=b.IndexOf((byte)0);if(end<0)end=b.Length;
        return end>0&&b[..end].ToArray().All(x=>x is >=32 and <=126)&&!b[end..].ContainsAnyExcept((byte)0);
    }
    internal static bool Accept(WireRecord r)
    {
        var v=r.Value;
        if(r.Type==0x1008&&v.Length==4)
        {int selector=BinaryPrimitives.ReadUInt16LittleEndian(v),slot=BinaryPrimitives.ReadUInt16LittleEndian(v.AsSpan(2));return selector==23&&slot<20||selector==41&&slot<10;}
        if(r.Type is 0x100c or 0x1015&&v.Length==20)
            return BinaryPrimitives.ReadUInt16LittleEndian(v)<(r.Type==0x100c?20:10)&&v[2]==0&&v[3]==0&&Label(v.AsSpan(4,16));
        if(r.Type==0x100a&&v.Length==4120&&BinaryPrimitives.ReadUInt16LittleEndian(v)<20&&v[2]==0&&v[3]==0&&Label(v.AsSpan(8,16)))
        {
            uint sum=0;foreach(byte b in v.AsSpan(24))sum+=b;
            if(sum!=BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4)))return false;
            for(int i=24;i<v.Length;i+=4)if(BinaryPrimitives.ReadInt32LittleEndian(v.AsSpan(i)) is <-8388608 or >8388607)return false;
            return true;
        }
        if(r.Type==0x1013&&v.Length==0x2018&&BinaryPrimitives.ReadUInt16LittleEndian(v)<10&&v[2]==0&&v[3]==0&&Label(v.AsSpan(8,16)))
        {
            uint sum=0;foreach(byte b in v.AsSpan(24))sum+=b;
            if(sum!=BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4)))return false;
            byte[] clo=new byte[0x2288];v.AsSpan(24).CopyTo(clo);
            try{_=new CloDocument(clo);return true;}catch(InvalidDataException){return false;}
        }
        return false;
    }
}
