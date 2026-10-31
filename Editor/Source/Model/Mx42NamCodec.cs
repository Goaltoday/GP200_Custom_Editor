using System.Buffers.Binary;
using System.Text;
namespace Gp200.Replica;
internal static class Mx42NamCodec
{
    internal const int Size=NamRecordLayout.Size,Weights=NamRecordLayout.WeightBytes;
    internal static void Name(string name){if(name.Length is <1 or >16||!name.Any(c=>c!=' ')||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not (' ' or '_' or '-')))throw new ArgumentException("NAM name: 1–16 ASCII letters, numbers, spaces, _ or -.");}
    internal static uint Crc(ReadOnlySpan<byte> bytes)=>Crc32.Compute(bytes);
    internal static byte[] Create(int slot,string name,byte[] weights)
    {
        Slot(slot);Name(name);if(weights.Length!=Weights)throw new InvalidDataException("Expected 1871 ordered NAM weights.");
        byte[] record=new byte[Size];BinaryPrimitives.WriteUInt32LittleEndian(record,NamRecordLayout.Magic);BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(NamRecordLayout.Slot),(uint)slot);BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(NamRecordLayout.WeightsCrc),Crc(weights));BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(NamRecordLayout.ArchitectureAt),NamRecordLayout.Architecture);Encoding.ASCII.GetBytes(name).CopyTo(record,NamRecordLayout.Name);weights.CopyTo(record,NamRecordLayout.Header);Validate(record,slot);return record;
    }
    internal static void Slot(int slot){if(slot is <0 or >31)throw new ArgumentOutOfRangeException(nameof(slot));}
    internal static string Header(ReadOnlySpan<byte> header,int slot,bool allowEmpty=true)
    {
        Slot(slot);if(header.Length!=NamRecordLayout.Header)throw new InvalidDataException("Invalid NAM header size.");
        if(!header.ContainsAnyExcept((byte)0)){if(allowEmpty)return "";throw new InvalidDataException("Empty NAM model.");}
        if(BinaryPrimitives.ReadUInt32LittleEndian(header)!=NamRecordLayout.Magic||BinaryPrimitives.ReadUInt32LittleEndian(header[NamRecordLayout.Slot..])!=slot||BinaryPrimitives.ReadUInt32LittleEndian(header[NamRecordLayout.ArchitectureAt..])!=NamRecordLayout.Architecture||header[NamRecordLayout.Reserved..].ContainsAnyExcept((byte)0))throw new InvalidDataException("Invalid NAM header magic, slot, architecture or reserved bytes.");
        var raw=header.Slice(NamRecordLayout.Name,NamRecordLayout.NameBytes);int end=raw.IndexOf((byte)0);if(end is <1 or >16||raw[end..].ContainsAnyExcept((byte)0))throw new InvalidDataException("Invalid NAM name padding.");string name=Encoding.ASCII.GetString(raw[..end]);Name(name);return name;
    }
    internal static void Validate(byte[] record,int slot,bool allowEmpty=false)
    {
        if(record.Length!=Size)throw new InvalidDataException("Invalid NAM record size.");string name=Header(record.AsSpan(0,NamRecordLayout.Header),slot,allowEmpty);
        if(name.Length==0){if(record.AsSpan().ContainsAnyExcept((byte)0))throw new InvalidDataException("Invalid empty NAM record.");return;}
        if(record.AsSpan(NamRecordLayout.Padding,sizeof(float)).ContainsAnyExcept((byte)0)||Crc(record.AsSpan(NamRecordLayout.Header,Weights))!=BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(NamRecordLayout.WeightsCrc)))throw new InvalidDataException("Invalid NAM weights CRC or padding.");
        for(int i=0;i<NamRecordLayout.WeightCount;i++)if(!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(NamRecordLayout.Header+sizeof(float)*i))))throw new InvalidDataException("Nonfinite NAM weight.");
        if(!(BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(NamRecordLayout.HeadScale))>0))throw new InvalidDataException("NAM head_scale must be positive.");
    }
}

