using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Gp200.Replica;

public sealed record FirmwareRegion(int DiskPair,int InternalIndex,uint RelativeOffset,int Offset,int Length,int End,string Sha256);
public sealed record FirmwareInspection(int Size,uint BodySize,string Model,byte Checksum,string Sha256,string VersionMarkerHex,string VersionMarker,bool ExtendedFlag,string Header48BytesHex,IReadOnlyList<FirmwareRegion> Regions);

/// <summary>Offline FRMW container inspection only, based on REA 0x470c20/0x471090.
/// This class has no device, transport, updater or executable-loading dependency.</summary>
public static class FirmwareInspector
{
    public static FirmwareInspection InspectFile(string path)
    {
        var file=new FileInfo(path);if(!file.Exists)throw new FileNotFoundException(UiText.T("Archivo firmware no encontrado."),path);
        // Local resource bound, not a limit claimed for the official updater.
        if(file.Length>128L*1024*1024)throw new InvalidDataException(UiText.T("El archivo supera el límite local de inspección de 128 MiB."));
        return Inspect(File.ReadAllBytes(path));
    }
    public static FirmwareInspection Inspect(ReadOnlySpan<byte> data,string model="GP-200")
    {
        if(data.Length<96)throw new InvalidDataException(UiText.T("Cabecera FRMW truncada."));
        if(!data[..4].SequenceEqual("FRMW"u8))throw new InvalidDataException(UiText.T("Firma FRMW incorrecta."));
        uint body=BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        if(body!=(uint)(data.Length-48))throw new InvalidDataException(UiText.T("El tamaño del cuerpo no coincide con el archivo."));
        if(model.Length>16||model.Any(c=>c>127))throw new ArgumentException(UiText.T("Modelo no válido."),nameof(model));
        Span<byte> expected=stackalloc byte[16];expected.Clear();Encoding.ASCII.GetBytes(model,expected);
        if(!data.Slice(16,16).SequenceEqual(expected))throw new InvalidDataException(UiText.T("El contenedor no corresponde al modelo ")+model+UiText.T("."));
        byte checksum=0;foreach(byte value in data[48..])checksum=unchecked((byte)(checksum+value));
        if(checksum!=data[14])throw new InvalidDataException(UiText.T("Checksum del cuerpo FRMW incorrecto."));
        int[] internalOrder=[0,3,1,2];var regions=new List<FirmwareRegion>(4);
        for(int pair=0;pair<4;pair++)
        {
            uint relative=BinaryPrimitives.ReadUInt32LittleEndian(data[(48+pair*8)..]);uint size=BinaryPrimitives.ReadUInt32LittleEndian(data[(52+pair*8)..]);
            long start=48L+relative,end=start+size;
            if(start<80||end>data.Length)throw new InvalidDataException(UiText.T("Componente fuera de los límites del contenedor."));
            regions.Add(new(pair,internalOrder[pair],relative,(int)start,(int)size,(int)end,Convert.ToHexString(SHA256.HashData(data.Slice((int)start,(int)size))).ToLowerInvariant()));
        }
        var ordered=regions.OrderBy(r=>r.Offset).ToArray();
        for(int i=1;i<ordered.Length;i++)if(ordered[i-1].End>ordered[i].Offset)throw new InvalidDataException(UiText.T("Los componentes firmware se solapan."));
        var marker=data.Slice(88,4);string printable=new(marker.ToArray().Select(b=>b is >=32 and <=126?(char)b:'.').ToArray());
        return new(data.Length,body,model,checksum,Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),Convert.ToHexString(marker).ToLowerInvariant(),printable,data[32]==1&&data.Slice(34,2).SequenceEqual("EX"u8),Convert.ToHexString(data[..48]).ToLowerInvariant(),regions);
    }
}
