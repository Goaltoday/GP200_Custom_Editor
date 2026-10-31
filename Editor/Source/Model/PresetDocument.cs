using System.Buffers.Binary;
using System.Text;

namespace Gp200.Replica;

public sealed record PresetRecord(ushort Type, int Offset, int Length);
public sealed record Effect(int Module, bool Enabled, uint Code, float[] Parameters);
public sealed record Assignment(byte Slot, byte Module, ushort Parameter, float End1, float End0);

/// <summary>Lossless file model. All offsets are TLV-relative, established by REA/Ghidra.
/// Unknown fields, sections, count variants and unused parameters are retained.
/// Local edits never invoke device transport.</summary>
public sealed class PresetDocument
{
    public static readonly string[] Modules = ["PRE", "WAH", "DST", "AMP", "NR", "CAB", "EQ", "MOD", "DLY", "RVB", "VOL"];
    private readonly byte[] data;
    public IReadOnlyList<PresetRecord> Records { get; }
    public string Name => Encoding.Latin1.GetString(data, Find(2,92).Offset + 20, 16).Split('\0')[0];
    public ushort Index => U16(Find(2,92).Offset + 4);
    public ushort Tempo => U16(Find(2,92).Offset + 6);
    public ushort Volume => U16(Find(2,92).Offset + 8);
    public bool ExpressionBankB=>data[Find(2,92).Offset+10]!=0;
    public sbyte Pan => unchecked((sbyte)data[Find(2,92).Offset+11]);
    public ushort PatchStyle => U16(Find(2,92).Offset+12);
    public ushort FxSendLevel => U16(Find(2,92).Offset+14);
    public ushort FxReturnLevel => U16(Find(2,92).Offset+16);
    public byte FxLoopMode => data[Find(2,92).Offset+18];
    public string Author => Encoding.Latin1.GetString(data,Find(2,92).Offset+36,16).Split('\0')[0];
    public string Notes => Encoding.Latin1.GetString(data,Find(2,92).Offset+52,40).Split('\0')[0];
    public byte[] Chain => data.AsSpan(Find(8,20).Offset+8,11).ToArray();
    public byte[] RoutingRaw => data.AsSpan(Find(8,20).Offset+6,2).ToArray();

    public static PresetDocument FromSnapshot(IReadOnlyList<WireRecord> records,ushort formatCode)
    {
        if(records.Count(r=>r.Type==0&&r.Value.Length==4)!=1)throw new InvalidDataException(UiText.T("Snapshot sin contexto único."));
        var bodyRecords=records.Where(r=>r.Type!=0).ToArray();
        if(bodyRecords.Count(r=>r.Type==2&&r.Value.Length==88)!=1||bodyRecords.Count(r=>r.Type==8&&r.Value.Length==16)!=1||
           bodyRecords.Count(r=>r.Type==20&&r.Value.Length==68)!=11||bodyRecords.Count(r=>r.Type==12&&r.Value.Length==12)!=9||
           bodyRecords.Count(r=>r.Type==16&&r.Value.Length==4)!=3||bodyRecords.Count(r=>r.Type==15&&r.Value.Length==8) is not (4 or 8))
            throw new InvalidDataException(UiText.T("Snapshot de preset incompleto o de otro formato."));
        if(!bodyRecords.Where(r=>r.Type==20).Select(r=>r.Value[0]).Order().SequenceEqual(Enumerable.Range(0,11).Select(i=>(byte)i)))throw new InvalidDataException(UiText.T("Módulos ambiguos en snapshot."));
        using var body=new MemoryStream();
        Span<byte> header=stackalloc byte[4];
        foreach(var record in bodyRecords)
        {
            if(record.Value.Length>ushort.MaxValue)throw new InvalidDataException(UiText.T("Registro demasiado grande."));
            BinaryPrimitives.WriteUInt16LittleEndian(header,record.Type);BinaryPrimitives.WriteUInt16LittleEndian(header[2..],(ushort)record.Value.Length);
            body.Write(header);body.Write(record.Value);
        }
        byte[] parameters=body.ToArray();byte[] file=new byte[parameters.Length+56];
        "TSRP"u8.CopyTo(file);BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10),formatCode);
        "2-PG"u8.CopyTo(file.AsSpan(16));BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20),0x100);BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22),1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32),40);BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(36),(uint)parameters.Length+4);
        "MRAP"u8.CopyTo(file.AsSpan(40));BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44),(uint)parameters.Length+4);parameters.CopyTo(file,48);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(file.Length-8),(uint)file.Length-8);
        uint checksum=0;foreach(byte b in file.AsSpan(0,file.Length-4))checksum+=b;BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(file.Length-4),checksum);
        // Header bytes 24..31 are zero by local policy. Native writer leaves
        // them unassigned; this is a generated PRST, not an original export.
        return new(file);
    }

    public PresetDocument(byte[] source)
    {
        data=(byte[])source.Clone();
        if (data.Length < 44 || !data.AsSpan(0,4).SequenceEqual("TSRP"u8) || !data.AsSpan(16,4).SequenceEqual("2-PG"u8))
            throw new InvalidDataException(UiText.T("Archivo PRST GP-200 no reconocido."));
        uint sum=0;
        foreach (byte b in data.AsSpan(0,data.Length-4)) sum=unchecked(sum+b);
        if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data.Length-4))!=sum)
            throw new InvalidDataException(UiText.T("Checksum PRST incorrecto."));
        int count=U16(22);
        if (count is <1 or >2) throw new InvalidDataException(UiText.T("Número de secciones no compatible."));
        var ranges=new List<(int Start,int End)>();
        var records=new List<PresetRecord>();
        int parameterSections=0;
        for (int n=0;n<count;n++)
        {
            uint rawStart=U32(32+8*n);
            if (rawStart > int.MaxValue) throw new InvalidDataException(UiText.T("Offset fuera del archivo."));
            int start=(int)rawStart;
            if (start<32+8*count || start>data.Length-12) throw new InvalidDataException(UiText.T("Sección fuera del archivo."));
            uint size=U32(start+4);
            if (size > data.Length-4L-start-8) throw new InvalidDataException(UiText.T("Sección truncada."));
            int end=checked(start+8+(int)size);
            ranges.Add((start,end));
            if (!data.AsSpan(start,4).SequenceEqual("MRAP"u8)) continue;
            parameterSections++;
            if (size<4) throw new InvalidDataException(UiText.T("Sección de parámetros truncada."));
            int position=start+8,bodyEnd=end-4;
            while (position<bodyEnd)
            {
                if (bodyEnd-position<4) throw new InvalidDataException(UiText.T("Cabecera TLV truncada."));
                ushort type=U16(position);
                int length=4+U16(position+2);
                if (length>bodyEnd-position) throw new InvalidDataException(UiText.T("Registro TLV truncado."));
                records.Add(new(type,position,length));
                position+=length;
            }
        }
        ranges.Sort((a,b)=>a.Start.CompareTo(b.Start));
        if (ranges.Zip(ranges.Skip(1)).Any(pair=>pair.First.End>pair.Second.Start))
            throw new InvalidDataException(UiText.T("Secciones solapadas."));
        if (parameterSections!=1) throw new InvalidDataException(UiText.T("Se esperaba una sección de parámetros."));
        Records=records.AsReadOnly();
        _=Find(2,92); _=Find(8,20);
    }

    private ushort U16(int at)=>BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at,2));
    private uint U32(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at,4));
    private float F32(int at)=>BitConverter.Int32BitsToSingle(unchecked((int)U32(at)));
    private void SetU16(int at,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at,2),value);
    private void SetU32(int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at,4),value);
    private void SetFloat(int at,float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentException(UiText.T("El valor debe ser finito."));
        SetU32(at,unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }
    public PresetRecord Find(ushort type,int size,Func<PresetRecord,bool>? predicate=null)
    {
        var found=Records.Where(r=>r.Type==type && r.Length>=size && (predicate?.Invoke(r)??true)).ToArray();
        if (found.Length!=1) throw new InvalidDataException(UiText.T($"Registro {type} ausente o ambiguo."));
        return found[0];
    }
    public byte ByteAt(int at)=>data[at];
    public IReadOnlyList<WireRecord> SnapshotRecords()=>Records.Select(r=>new WireRecord(r.Type,data.AsSpan(r.Offset+4,r.Length-4).ToArray())).ToArray();
    public Effect GetEffect(int module)
    {
        var r=Find(20,72,r=>data[r.Offset+4]==module);
        return new(module,data[r.Offset+5]!=0,U32(r.Offset+8),Enumerable.Range(0,15).Select(i=>F32(r.Offset+12+i*4)).ToArray());
    }
    public Assignment[] Expression=>Records.Where(r=>r.Type==12&&r.Length>=16).Select(r=>new Assignment(data[r.Offset+4],data[r.Offset+5],U16(r.Offset+6),F32(r.Offset+8),F32(r.Offset+12))).ToArray();
    public Assignment[] Quick=>Records.Where(r=>r.Type==16&&r.Length>=8).Select(r=>new Assignment(data[r.Offset+4],data[r.Offset+5],U16(r.Offset+6),0,0)).ToArray();
    public (byte Slot,uint Mask)[] Controls=>Records.Where(r=>r.Type==15&&r.Length>=12).Select(r=>(data[r.Offset+4],U32(r.Offset+8))).ToArray();

    public void SetGeneral(string name,int volume,int tempo)
    {
        // UI policy: printable ASCII; do not claim recovered Unicode equivalence.
        if (name.Length is <1 or >16 || name.Any(c=>c<32||c>126)) throw new ArgumentException(UiText.T("Nombre: 1–16 caracteres ASCII imprimibles."));
        if (volume is <0 or >100 || tempo is <40 or >250) throw new ArgumentException(UiText.T("Volumen 0–100; tempo 40–250."));
        int at=Find(2,92).Offset;
        data.AsSpan(at+20,16).Clear(); Encoding.ASCII.GetBytes(name).CopyTo(data,at+20);
        SetU16(at+8,(ushort)volume); SetU16(at+6,(ushort)tempo);
    }
    public void SetPatchOptions(int? sendLevel=null,int? returnLevel=null,int? loopMode=null,int? pan=null)
    {
        if(loopMode.HasValue&&FxLoopMode>1)throw new InvalidOperationException("Extended Custom routing is preserved; changing its mode is not supported yet.");
        if(sendLevel is <0 or >100||returnLevel is <0 or >100||loopMode is <0 or >1||pan is <-100 or >100)
            throw new ArgumentException(UiText.T("FX Loop: niveles 0–100; modo 0 Parallel / 1 Series. Pan: −100…100."));
        int at=Find(2,92).Offset;
        if(pan.HasValue)data[at+11]=unchecked((byte)(sbyte)pan.Value);
        if(sendLevel.HasValue)SetU16(at+14,(ushort)sendLevel.Value);
        if(returnLevel.HasValue)SetU16(at+16,(ushort)returnLevel.Value);
        if(loopMode.HasValue)data[at+18]=(byte)loopMode.Value;
    }
    public void SetPatchStyle(int style)
    {
        if(style is <0 or >19)throw new ArgumentOutOfRangeException(nameof(style));SetU16(Find(2,92).Offset+12,(ushort)style);
    }
    public void SetEffect(int module,bool enabled,float[] parameters)
    {
        if (parameters.Length!=15) throw new ArgumentException(UiText.T("Se esperaban 15 valores."));
        int at=Find(20,72,r=>data[r.Offset+4]==module).Offset;
        for(int p=0;p<15;p++)
        {
            float previous=F32(at+12+4*p),value=parameters[p];
            if(!float.IsFinite(value)&&!(float.IsNaN(value)&&float.IsNaN(previous))&&BitConverter.SingleToInt32Bits(value)!=BitConverter.SingleToInt32Bits(previous))
                throw new ArgumentException(UiText.T("Los valores nuevos deben ser finitos; se conservan los datos no finitos existentes."));
        }
        data[at+5]=enabled?(byte)1:(byte)0;
        for(int p=0;p<15;p++)
        {
            // Preserve original NaN payload bits, including unused/legacy slots.
            if(!float.IsFinite(parameters[p]))continue;
            if(BitConverter.SingleToInt32Bits(parameters[p])!=BitConverter.SingleToInt32Bits(F32(at+12+4*p)))SetFloat(at+12+4*p,parameters[p]);
        }
    }
    public void SetMetadata(string author,string notes)
    {
        if(author.Length>16||notes.Length>40||author.Concat(notes).Any(c=>c<32||c>126))throw new ArgumentException(UiText.T("Autor: hasta 16 caracteres; notas: hasta 40. Usa ASCII imprimible."));
        int at=Find(2,92).Offset;
        data.AsSpan(at+36,56).Clear();Encoding.ASCII.GetBytes(author).CopyTo(data,at+36);Encoding.ASCII.GetBytes(notes).CopyTo(data,at+52);
    }
    public void SetChain(IEnumerable<byte> order)
    {
        byte[] values=order.ToArray();
        if (!values.Order().SequenceEqual(Enumerable.Range(0,11).Select(i=>(byte)i))) throw new ArgumentException(UiText.T("La cadena debe contener una vez cada módulo 0–10."));
        values.CopyTo(data,Find(8,20).Offset+8);
    }
    public void SetCtrl(byte slot,uint mask)
    {
        if(mask>0x7ff) throw new ArgumentException(UiText.T("Máscara CTRL fuera de rango."));
        int at=Find(15,12,r=>data[r.Offset+4]==slot).Offset+8;
        SetU32(at,(U32(at)&~0x7ffu)|mask);
    }
    public void SetAlgorithm(int module,AlgorithmInfo algorithm,bool matchCabinet=false)
    {
        if(!algorithm.ParametersKnown)throw new InvalidOperationException("Custom parameter metadata is unavailable for this effect.");
        if(module<0||module>=11||(algorithm.PhysicalSlot>=0?algorithm.PhysicalSlot!=module:algorithm.Module!=Modules[module]))throw new ArgumentException(UiText.T("Algoritmo de otro módulo."));
        bool clearUniversalBindings=algorithm.PhysicalSlot>=0&&(algorithm.IsNone||CatalogRuntime.Current.Identity(module,GetEffect(module).Code).EffectiveFamily!=algorithm.FamilyId);
        var defaults=algorithm.Parameters.Select(p=>(p.Slot,Value:Catalog.DefaultValue(p.Default))).ToArray();
        if(defaults.Any(p=>p.Slot<0||p.Slot>=15))throw new ArgumentException(UiText.T("ID de parámetro fuera de rango."));
        int effect=Find(20,72,r=>data[r.Offset+4]==module).Offset;
        int? cabinet=matchCabinet&&module==3&&algorithm.CabinetCode.HasValue?Find(20,72,r=>data[r.Offset+4]==5).Offset:null;
        SetU32(effect+8,algorithm.Code);
        // Policy: preserve bytes for slots absent from XML. The native constructor
        // does not establish zero defaults for every unused slot.
        foreach(var parameter in defaults)SetFloat(effect+12+4*parameter.Slot,parameter.Value);
        foreach(var record in Records.Where(r=>(r.Type==12&&r.Length>=16||r.Type==16&&r.Length>=8)&&data[r.Offset+5]==module))SetU16(record.Offset+6,0);
        // Local optional CAB association; live firmware defaults are modeled separately.
        if(cabinet.HasValue)SetU32(cabinet.Value+8,algorithm.CabinetCode!.Value);
        if(clearUniversalBindings){foreach(var q in Quick.Where(x=>x.Module==module).ToArray())SetDestination(true,q.Slot,255,0,CatalogRuntime.Current);foreach(var a in Expression.Where(x=>x.Module==module).ToArray()){SetDestination(false,a.Slot,255,0,CatalogRuntime.Current);SetExpression(a.Slot,0,0);}}
    }
    internal void SetMatchedCabinetCode(uint code)=>SetU32(Find(20,72,r=>data[r.Offset+4]==5).Offset+8,code);
    internal void SetMatchedCabinet(AlgorithmInfo cabinet,Effect? previous=null)
    {
        SetMatchedCabinetCode(cabinet.Code);
        int offset=Find(20,72,r=>data[r.Offset+4]==5).Offset;
        // Captured automatic AMP matching resets declared CAB controls while
        // retaining bypass, unknown slots and assignments.
        var requested=GetEffect(5);
        foreach(var parameter in cabinet.Parameters)
            if(previous==null||BitConverter.SingleToInt32Bits(previous.Parameters[parameter.Slot])==BitConverter.SingleToInt32Bits(requested.Parameters[parameter.Slot]))
                SetFloat(offset+12+4*parameter.Slot,Catalog.DefaultValue(parameter.Default));
    }
    public void SetLoopNode(bool returnNode,int position)
    {
        if(FxLoopMode>1&&CatalogRuntime.Current.IsMx42)
        {
            int s=RoutingRaw[0],r=RoutingRaw[1];if(returnNode){r=position;s=Math.Min(s,r);}else{s=position;r=Math.Max(r,s);}
            int p=Math.Clamp(ChainRouting.Boundary(this),s,r);SetCustomRouting(FxLoopMode,s,p,r);return;
        }
        if(FxLoopMode>1)throw new InvalidOperationException("Extended Custom routing is preserved; moving Send/Return requires support for its Pedal boundary.");
        // Twelve gaps around eleven modules: conservative local UI policy.
        if(position is <0 or >11)throw new ArgumentOutOfRangeException(nameof(position));
        int at=Find(8,20).Offset;
        if(returnNode){data[at+7]=(byte)position;if(data[at+6]>position)data[at+6]=(byte)position;}
        else{data[at+6]=(byte)position;if(data[at+7]<position)data[at+7]=(byte)position;}
    }
    internal void SetCustomRouting(int mode,int send,int boundary,int ret)
    {
        Mx42Policy.RequireScenes();
        if(!ChainRouting.ValidMode(mode)||send<0||send>boundary||boundary>ret||ret>11)throw new ArgumentException("CHAIN requires 0 ≤ Send ≤ Split ≤ Return ≤ 11.");
        if(ChainRouting.Extended(mode))mode=(mode&0xf0)|boundary;
        data[Find(2,92).Offset+18]=(byte)mode;int at=Find(8,20).Offset;data[at+6]=(byte)send;data[at+7]=(byte)ret;
        if(CatalogRuntime.Current.IsMx42&&ChainRouting.Extended(mode)){var c=Mx42ChainConfig.GetOrDefault(this);(c with{Start=send,Split=boundary,End=ret}).Write(this);}
    }
    public void SetExpression(byte slot,float end1,float end0)
    {
        if(!float.IsFinite(end1)||!float.IsFinite(end0)) throw new ArgumentException(UiText.T("Extremos no finitos."));
        int at=Find(12,16,r=>data[r.Offset+4]==slot).Offset;
        SetFloat(at+8,end1); SetFloat(at+12,end0);
    }
    public void SetDestination(bool quick,byte slot,byte module,ushort parameter,Catalog? catalog=null)
    {
        if(module<11)catalog?.RequireParameters(module,GetEffect(module).Code);
        if(catalog?.IsMx42==true&&module<11&&catalog.Find(module,GetEffect(module).Code)?.Parameters.Any(p=>p.Slot==parameter)!=true)throw new InvalidOperationException("MX42 assignment must target a declared parameter of this instance.");
        bool special=quick&&module is 11 or 12;
        if(special?parameter is not (0 or 255):module!=255&&module>10||parameter>14)throw new ArgumentOutOfRangeException(nameof(module));
        int at=Find(quick?(ushort)16:(ushort)12,quick?8:16,r=>data[r.Offset+4]==slot).Offset;
        data[at+5]=module;SetU16(at+6,parameter);
        if(!quick)
        {
            if(module==255){SetFloat(at+8,0);SetFloat(at+12,0);}
            else if(catalog?.Find(module,GetEffect(module).Code)?.Parameters.FirstOrDefault(p=>p.Slot==parameter) is ParameterInfo info)
            {var bounds=info.Bounds(GetEffect(module));SetFloat(at+8,bounds.Maximum);SetFloat(at+12,bounds.Minimum);}
        }
    }
    public void SetParameter(int module,int slot,float value,Catalog catalog)
    {
        catalog.RequireParameters(module,GetEffect(module).Code);
        if(catalog.IsMx42&&catalog.Find(module,GetEffect(module).Code)?.Parameters.Any(p=>p.Slot==slot)!=true)throw new InvalidOperationException("This word is not a DSP control for the current MX42 instance.");
        if(slot is <0 or >14||!float.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(slot));
        var effect=GetEffect(module);var values=(float[])effect.Parameters.Clone();values[slot]=value;
        var algorithm=catalog.Find(module,effect.Code);var changed=algorithm?.Parameters.FirstOrDefault(p=>p.Slot==slot);
        if(changed?.Kind=="Switch"&&changed.Power&&values[slot]!=effect.Parameters[slot]&&algorithm!=null)
            foreach(var bound in algorithm.Parameters.Where(p=>p.Bind==slot))values[bound.Slot]=value>0?4:Catalog.DefaultValue(bound.Default);
        SetEffect(module,effect.Enabled,values);
        if(catalog.IsMx42&&changed?.Power==true&&value!=effect.Parameters[slot]&&algorithm!=null)
        {
            var bound=algorithm.Parameters.Where(p=>p.Bind==slot&&!p.Power).Select(p=>p.Slot).ToHashSet();
            foreach(var assignment in Expression.Where(a=>a.Module==module&&bound.Contains(a.Parameter)))
                SetDestination(false,assignment.Slot,assignment.Module,assignment.Parameter,catalog);
        }
    }
    public byte[] ToBytes()
    {
        byte[] output=(byte[])data.Clone(); uint sum=0;
        foreach(byte b in output.AsSpan(0,output.Length-4))sum=unchecked(sum+b);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(output.Length-4),sum);
        return output;
    }
}
