using System.Buffers.Binary;
using System.Text;

namespace Gp200.Replica;

public sealed record IrSlot(int Index, ushort State, uint Checksum, string Name)
{
    public override string ToString()=>$"USER IR {Index+1:00} · {Name}";
}
public sealed record IrImport(WireRecord Record,int SourceFrames,int RetainedFrames,int Bits,int Rate,int ConvertedFrames=-1,int SourceChannels=1)
{
    public int DiscardedFrames=>Math.Max(0,(ConvertedFrames<0?SourceFrames:ConvertedFrames)-1024);
}

/// <summary>Native 5a9bd0/5aa000 import and 5ba8d0 rename, retained REA in NAM-IR-SPEC.
/// This codec never opens MIDI and never creates firmware/SnapTone messages.</summary>
public static class IrService
{
    public static string CanonicalName(string name)=>name.TrimEnd(' ');
    public static bool ImportConfirmed(IrSlot actual,int slot,string name)=>actual.Index==slot&&actual.State==0&&CanonicalName(actual.Name)==CanonicalName(name);
    public static async Task<IrSlot> ConfirmImportAsync(Func<Task<IReadOnlyList<WireRecord>>> read,int slot,string name)
    {
        var attempts=new List<IrSlot>();var timer=System.Diagnostics.Stopwatch.StartNew();IrSlot? actual=null;
        do
        {
            actual=Catalog(await read()).Single(s=>s.Index==slot);attempts.Add(actual);
            if(ImportConfirmed(actual,slot,name))break;
            await Task.Delay(150);
        }while(timer.ElapsedMilliseconds<2500);
        string folder=AppPaths.Evidence;
        EditorAuditLog.Supplement(Path.Combine(folder,$"ir-verification-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json"),new{slot,name,attempts,confirmed=actual!=null&&ImportConfirmed(actual,slot,name),writes=0});
        if(actual==null||!ImportConfirmed(actual,slot,name))throw new IOException(UiText.T("Envío realizado, pero el catálogo aún no confirma la carga. Usa Comprobar carga; no hace falta reenviar el IR."));
        return actual;
    }
    public static IReadOnlyList<IrSlot> Catalog(IEnumerable<WireRecord> records)
    {
        var slots=new Dictionary<int,IrSlot>();
        foreach(var r in records.Where(r=>r.Type==0x1009))
        {
            if(r.Value.Length!=24)throw new InvalidDataException(UiText.T("Registro de catálogo IR de longitud desconocida."));
            var v=r.Value;int slot=BinaryPrimitives.ReadUInt16LittleEndian(v);
            if(slot>=20||slots.ContainsKey(slot))throw new InvalidDataException(UiText.T("Índice IR duplicado o fuera del banco de veinte slots."));
            slots.Add(slot,new(slot,BinaryPrimitives.ReadUInt16LittleEndian(v.AsSpan(2)),BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4)),Encoding.Latin1.GetString(v,8,16).Split('\0')[0]));
        }
        return slots.Values.OrderBy(s=>s.Index).ToArray();
    }
    static byte[] Label(string name)
    {
        name=CanonicalName(name);
        if(name.Length is <1 or >16||name.Any(c=>c<32||c>126))throw new ArgumentException(UiText.T("Nombre de 1 a 16 caracteres ASCII imprimibles."));
        byte[] data=new byte[16];Encoding.ASCII.GetBytes(name).CopyTo(data,0);return data;
    }
    static void Slot(int slot){if(slot is <0 or >=20)throw new ArgumentOutOfRangeException(nameof(slot));}
    public static WireRecord Rename(int slot,string name)
    {
        Slot(slot);byte[] v=new byte[20];BinaryPrimitives.WriteUInt16LittleEndian(v,(ushort)slot);Label(name).CopyTo(v,4);
        // Native leaves reserved WORD unassigned. Canonical zero is our policy.
        return new(0x100c,v);
    }
    public static WireRecord Delete(int slot)
    {
        Slot(slot);byte[] value=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(value,23);BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(2),(ushort)slot);return new(0x1008,value);
    }
    public static WireRecord FromPcm24(ReadOnlySpan<byte> pcm,int slot,string name)
    {
        Slot(slot);if(pcm.Length%3!=0)throw new InvalidDataException(UiText.T("Muestra PCM24 incompleta."));
        byte[] value=new byte[4120];BinaryPrimitives.WriteUInt16LittleEndian(value,(ushort)slot);Label(name).CopyTo(value,8);
        int count=Math.Min(1024,pcm.Length/3);
        for(int i=0;i<count;i++)
        {
            int n=pcm[i*3]|pcm[i*3+1]<<8|pcm[i*3+2]<<16;
            if((n&0x800000)!=0)n|=unchecked((int)0xff000000);
            BinaryPrimitives.WriteInt32LittleEndian(value.AsSpan(24+i*4),n);
        }
        uint sum=0;foreach(byte b in value.AsSpan(24))sum+=b;
        BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4),sum);return new(0x100a,value);
    }
    public static IrImport ReadWav(string path,int slot,string name,bool useLeftChannelForStereo24=false)
    {
        using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
        string Four()=>Encoding.ASCII.GetString(reader.ReadBytes(4));
        if(file.Length<12||Four()!="RIFF")throw new InvalidDataException(UiText.T("Se requiere un WAV RIFF PCM."));
        uint declared=reader.ReadUInt32();if((long)declared+8>file.Length||Four()!="WAVE")throw new InvalidDataException(UiText.T("Cabecera WAV truncada."));
        ushort format=0,channels=0,bits=0,align=0;uint rate=0;long dataAt=-1;uint dataSize=0;long end=(long)declared+8;
        while(file.Position+8<=end)
        {
            string id=Four();uint size=reader.ReadUInt32();long next=file.Position+size+(size&1);
            if(next>end)throw new InvalidDataException(UiText.T("Chunk WAV truncado."));
            if(id=="fmt ")
            {
                if(size<16)throw new InvalidDataException(UiText.T("Formato WAV truncado."));
                format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadUInt32();reader.ReadUInt32();align=reader.ReadUInt16();bits=reader.ReadUInt16();
                if(format==0xfffe)
                {
                    if(size<40||reader.ReadUInt16()<22)throw new InvalidDataException(UiText.T("WAVE_FORMAT_EXTENSIBLE truncado."));
                    ushort validBits=reader.ReadUInt16();reader.ReadUInt32();byte[] guid=reader.ReadBytes(16);
                    byte[] canonicalTail=[0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71];
                    uint subtype=BinaryPrimitives.ReadUInt32LittleEndian(guid);
                    if(!guid.AsSpan(4).SequenceEqual(canonicalTail)||subtype is not (1 or 3)||(validBits!=0&&validBits!=bits))throw new NotSupportedException(UiText.T("Subformato WAV extensible no soportado."));
                    format=(ushort)subtype;
                }
            }
            else if(id=="data"&&dataAt<0){dataAt=file.Position;dataSize=size;}
            file.Position=next;
        }
        bool integer=format==1&&bits is 16 or 24 or 32;bool floating=format==3&&bits==32;
        if((!integer&&!floating)||channels is <1 or >2||rate is <1000 or >768000||align!=bits/8*channels)
            throw new NotSupportedException(UiText.T($"WAV {channels} canales / {rate} Hz / {bits} bits. Se requiere mono o estéreo, PCM16/24/32 o float32, entre 1000 y 768000 Hz."));
        if(channels==2&&integer&&bits==24&&rate==44100&&!useLeftChannelForStereo24)
            throw new NotSupportedException(UiText.T("El editor oficial trata de forma inconsistente el WAV estéreo PCM24/44100 en su ruta rápida. Exporta un WAV mono para importar ese formato sin mezclar las muestras de ambos canales."));
        int sampleBytes=bits/8;
        if(dataAt<0||dataSize%align!=0||dataSize/align>int.MaxValue)throw new InvalidDataException(UiText.T("Datos WAV ausentes o muestras incompletas."));
        if(rate!=44100&&dataSize>64*1024*1024)throw new InvalidDataException(UiText.T("El WAV supera el límite local de 64 MiB para conversión IR."));
        file.Position=dataAt;int frames=(int)(dataSize/align);byte[] source=reader.ReadBytes((int)(rate==44100?Math.Min(1024*align,dataSize):dataSize));
        int convertedFrames=rate==44100?frames:(int)((float)frames*44100f/(float)rate);
        byte[] pcm;
        if(integer&&bits==24&&rate==44100)
        {
            if(channels==1)pcm=source;
            else {pcm=new byte[source.Length/align*3];for(int i=0;i<source.Length/align;i++)source.AsSpan(i*align,3).CopyTo(pcm.AsSpan(i*3));}
        }
        else
        {
            float[] samples=new float[source.Length/align];
            for(int i=0;i<source.Length/align;i++)
            {
                int offset=i*align; // Native mono AudioBuffer receives the left channel only.
                float sample=floating?BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(offset)):
                    bits==16?(float)(BinaryPrimitives.ReadInt16LittleEndian(source.AsSpan(offset))<<16)*4.656612873077392578125e-10f:
                    bits==24?(float)((source[offset]|source[offset+1]<<8|source[offset+2]<<16)<<8)*4.656612873077392578125e-10f:
                    (float)BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset))*4.656612873077392578125e-10f;
                if(!float.IsFinite(sample))throw new InvalidDataException(UiText.T("El WAV contiene una muestra no finita."));
                samples[i]=sample;
            }
            if(rate!=44100)samples=LegacyIrResampler.Convert(samples,(int)rate,Math.Min(1024,convertedFrames));
            pcm=new byte[samples.Length*3];
            for(int i=0;i<samples.Length;i++)
            {
                int quantized=QuantizeFloat(samples[i])>>8;
                pcm[i*3]=(byte)quantized;pcm[i*3+1]=(byte)(quantized>>8);pcm[i*3+2]=(byte)(quantized>>16);
            }
        }
        return new(FromPcm24(pcm,slot,name),frames,Math.Min(1024,convertedFrames),bits,(int)rate,convertedFrames,channels);
    }
    // 6118e0 int32->float scaling, 47c930+47c6b0 equal-rate bypass,
    // 610cf0 round-to-int32 magic-bias operation. WAV24 keeps the high 24 bits.
    internal static int QuantizeFloat(float sample)
    {
        if(!float.IsFinite(sample))throw new ArgumentException(UiText.T("Muestra no finita."));
        if(sample<=-1)return int.MinValue;if(sample>=1)return int.MaxValue;
        return unchecked((int)BitConverter.DoubleToInt64Bits((double)sample*2147483647.0+6755399441055744.0));
    }
    public static byte[] LogicalPayload(WireRecord record)
    {
        byte[] b=new byte[record.Value.Length+4];BinaryPrimitives.WriteUInt16LittleEndian(b,record.Type);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2),(ushort)record.Value.Length);record.Value.CopyTo(b,4);return b;
    }
}
