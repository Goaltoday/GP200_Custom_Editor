using System.Buffers.Binary;
using System.Text;
namespace Gp200.Replica;
/// <summary>Final GP-200 CLO, preserved byte-exactly after Tone Match.</summary>
public sealed class CloDocument
{
    readonly byte[] data;
    public byte[] ToBytes()=>(byte[])data.Clone();
    public CloDocument(byte[] bytes)
    {
        if(bytes.Length!=0x2288||!bytes.AsSpan(0,4).SequenceEqual("VTSI"u8))throw new InvalidDataException("Expected a padded GP-200 VTSI CLO (8840 bytes).");
        uint U(int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        if(U(4)!=0x1288||U(0x14)!=0x1200||U(0x78)!=0||U(0x7c)!=128||U(0x80)!=128||U(0x84)!=1024)throw new InvalidDataException("Expected final GP-200 A128/B1024 CLO. Other models are not silently truncated.");
        if(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8))!=Crc(bytes.AsSpan(12,0x1288-12)))throw new InvalidDataException("CLO CRC mismatch.");
        for(int at=0x88;at<0x1288;at+=4)if(!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at))))throw new InvalidDataException("Non-finite CLO coefficient.");
        for(int at=0x18;at<0x68;at+=8)if(!double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(at))))throw new InvalidDataException("Non-finite CLO filter.");
        for(int at=0x68;at<0x78;at+=4)if(!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at))))throw new InvalidDataException("Non-finite CLO P/K.");
        if(bytes.AsSpan(0x1288).IndexOfAnyExcept((byte)0)>=0)throw new InvalidDataException("CLO padding is not zero.");data=(byte[])bytes.Clone();
    }
    internal static ushort Crc(ReadOnlySpan<byte> bytes)
    {ushort crc=65535;foreach(byte b in bytes){crc^=b;for(int bit=0;bit<8;bit++)crc=(ushort)((crc&1)!=0?(crc>>1)^0xa001:crc>>1);}return crc;}
    public WireRecord UploadRecord(int slot,string name)
    {
        if(slot is <0 or >9)throw new ArgumentOutOfRangeException(nameof(slot));
        if(name.Length is <1 or >16||name.Any(c=>c<32||c>126))throw new ArgumentException("Name: 1–16 printable ASCII characters.");
        byte[] value=new byte[0x2018];BinaryPrimitives.WriteUInt16LittleEndian(value,(ushort)slot);
        uint sum=0;foreach(byte b in data.AsSpan(0,8192))sum+=b;
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4),sum);Encoding.ASCII.GetBytes(name).CopyTo(value,8);data.AsSpan(0,8192).CopyTo(value.AsSpan(24));return new(0x1013,value);
    }
}
