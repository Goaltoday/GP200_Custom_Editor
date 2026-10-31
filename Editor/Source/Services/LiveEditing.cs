using System.Buffers.Binary;

namespace Gp200.Replica;

/// <summary>Typed edits of the current volatile preset, followed by explicit readback.</summary>
public sealed class LiveEditing
{
    static Catalog SharedCatalog=>CatalogRuntime.Current;
    readonly DeviceSession session;
    internal DeviceSession Session=>session;
    readonly ushort format;
    public bool IsConnected=>session.Connected;
    public PresetDocument Baseline {get;private set;}
    public bool AutoCabinetMatch {get;private set;}
    DeviceSettings? operatingSettings;
    public ExpressionOperatingState? ExpressionState=>operatingSettings==null?null:ExpressionOperatingState.From(Baseline,operatingSettings);
    public async Task<ExpressionOperatingState?> RefreshExpressionStateAsync(){using var context=session.Enter();await DeviceMidi.OperationGate.WaitAsync();try{var response=await session.Read(ReadQuery.State);Check(response);operatingSettings=new DeviceSettings(response.Records);AutoCabinetMatch=operatingSettings.Global(36)!=0;return ExpressionState;}finally{DeviceMidi.OperationGate.Release();}}
    LiveEditing(PresetDocument document,ushort format,DeviceSession session){Baseline=document;this.format=format;this.session=session;}
    public static Task<LiveEditing> ConnectAsync()=>ConnectAsync(DeviceSession.Current);
    // A gated transaction already negotiated capabilities at connection time.
    // Refresh its live state without fetching the entire NAM inventory again.
    internal static Task<LiveEditing> ConnectCurrentAsync()=>ConnectAsync(DeviceSession.Current,false);
    internal static async Task<LiveEditing> ConnectAsync(DeviceSession session,bool discoverIntegration=true)
    {
        using var context=session.Enter();
        try
        {
        var identity=await session.Read(ReadQuery.Identity);Check(identity);
        var state=await session.Read(ReadQuery.State);Check(state);
        ushort format=BinaryPrimitives.ReadUInt16LittleEndian(state.Records.Single(r=>r.Type==0x1007).Value);
        if(discoverIntegration&&SharedCatalog.IsMx42){await Mx42Integration.Probe();await Mx42Integration.ReadNamNames(SharedCatalog);}
        var active=await session.Read(ReadQuery.ActivePreset);Check(active);
        var settings=new DeviceSettings(state.Records);
        var document=PresetDocument.FromSnapshot(active.Records,format);if(Mx42ChainUsb.Available)Mx42ChainUsb.Normalize(document,await Mx42ChainUsb.Read());
        session.Format=format;session.Connected=true;
        return new(document,format,session){AutoCabinetMatch=settings.Global(36)!=0,operatingSettings=settings};
        }
        catch{session.Reset();throw;}
    }
    internal LiveEditing ForSelectedPreset(PresetDocument selected)=>new(selected,format,session){AutoCabinetMatch=AutoCabinetMatch,operatingSettings=operatingSettings};
    internal static PresetDocument ExpectedAfterAutoCab(PresetDocument before,PresetDocument requested,bool enabled,Catalog? catalog=null)
    {
        catalog??=SharedCatalog;
        if(catalog.IsMx42)return requested;
        if(!enabled||before.GetEffect(3).Code==requested.GetEffect(3).Code||before.GetEffect(5).Code!=requested.GetEffect(5).Code)return requested;
        var amp=catalog.Find(3,requested.GetEffect(3).Code);
        if(amp?.CabinetCode is not uint cab||catalog.Find(5,cab)==null)return requested;
        var expected=new PresetDocument(requested.ToBytes());expected.SetMatchedCabinet(catalog.Find(5,cab)!,before.GetEffect(5));return expected;
    }
    static void Check(DeviceResponse response){if(response.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver no liberó correctamente los puertos MIDI."));}
    static bool Same(PresetDocument a,PresetDocument b)
    {
        return PresetComparison.EquivalentFunctional(a,b,SharedCatalog);
    }
    public async Task<PresetDocument> ReadCurrentAsync()
    {
        using var context=session.Enter();await DeviceMidi.OperationGate.WaitAsync();try{if(!IsConnected)throw new IOException(UiText.T("La sesión se ha interrumpido. Vuelve a conectar para leer el estado real; no se reenviarán cambios pendientes."));var response=await session.Read(ReadQuery.ActivePreset);Check(response);return await Normalize(response);}catch{session.Reset();throw;}finally{DeviceMidi.OperationGate.Release();}
    }
    async Task<PresetDocument> Normalize(DeviceResponse response)
    {var p=PresetDocument.FromSnapshot(response.Records,format);if(Mx42ChainUsb.Available)Mx42ChainUsb.Normalize(p,await Mx42ChainUsb.Read());return p;}
    public void AcceptSnapshot(PresetDocument document)=>Baseline=document;
    internal static List<byte[]> Build(PresetDocument before,PresetDocument after,Catalog? catalog=null)
    {
        catalog??=SharedCatalog;
        if(catalog.IsMx42)
        {
            var departing=Enumerable.Range(0,11).Where(m=>
            {
                var a=catalog.Identity(m,before.GetEffect(m).Code);var b=catalog.Identity(m,after.GetEffect(m).Code);
                return a.EffectiveFamily==11&&!a.IsNone&&(b.EffectiveFamily!=11||b.IsNone);
            }).ToArray();
            bool entering=Enumerable.Range(0,11).Any(m=>
            {
                var a=catalog.Identity(m,before.GetEffect(m).Code);var b=catalog.Identity(m,after.GetEffect(m).Code);
                return b.EffectiveFamily==11&&!b.IsNone&&(a.EffectiveFamily!=11||a.IsNone);
            });
            if(departing.Length>0&&entering)
            {
                // A valid final preset can still violate single-NAM admission in
                // transit. Release the old selection first, including bindings,
                // then use the same verified diff/readback route for the target.
                var released=new PresetDocument(before.ToBytes());
                foreach(int m in departing)released.SetAlgorithm(m,catalog.ForFamily(m,11)[^1]);
                var ordered=BuildCore(before,released,catalog);ordered.AddRange(BuildCore(released,after,catalog));return ordered;
            }
        }
        return BuildCore(before,after,catalog);
    }
    static List<byte[]> BuildCore(PresetDocument before,PresetDocument after,Catalog catalog)
    {
        if(before.Index!=after.Index)throw new InvalidOperationException(UiText.T("Usa la biblioteca para seleccionar otra posición."));
        var edits=new List<byte[]>();var left=before.SnapshotRecords();var right=after.SnapshotRecords();
        if(left.Count!=right.Count||left.Zip(right).Any(p=>p.First.Type!=p.Second.Type||p.First.Value.Length!=p.Second.Value.Length))throw new InvalidDataException(UiText.T("Ha cambiado la estructura del preset."));
        var aGeneral=left.Single(r=>r.Type==2).Value;var bGeneral=right.Single(r=>r.Type==2).Value;var covered=(byte[])aGeneral.Clone();
        foreach(var (offset,selector,size) in new[]{(2,1,2),(4,0,2),(7,6,1),(8,2,2),(10,3,2),(12,4,2),(14,5,1)})
        {
            if(aGeneral.AsSpan(offset,size).SequenceEqual(bGeneral.AsSpan(offset,size)))continue;
            ushort value=size==1?(ushort)bGeneral[offset]:BinaryPrimitives.ReadUInt16LittleEndian(bGeneral.AsSpan(offset));
            if(selector==6)value=unchecked((ushort)(short)(sbyte)bGeneral[offset]);
            edits.Add(Frame(6,Scalar((ushort)selector,value)));bGeneral.AsSpan(offset,size).CopyTo(covered.AsSpan(offset));
        }
        foreach(var (offset,length,type) in new[]{(16,16,7),(32,16,9),(48,40,11)})
        {
            if(aGeneral.AsSpan(offset,length).SequenceEqual(bGeneral.AsSpan(offset,length)))continue;
            byte[] value=new byte[4+length];BinaryPrimitives.WriteUInt16LittleEndian(value,after.Index);bGeneral.AsSpan(offset,length).CopyTo(value.AsSpan(4));
            edits.Add(Frame((ushort)type,value));bGeneral.AsSpan(offset,length).CopyTo(covered.AsSpan(offset));
        }
        covered[15]=bGeneral[15]; // Observed volatile byte; no partial setter.
        covered[6]=bGeneral[6]; // Current EXP A/B state belongs to the received preset.
        if(!covered.SequenceEqual(bGeneral))throw new InvalidOperationException(UiText.T("El cambio incluye campos generales sin un mensaje de edición verificado."));
        // After the optional release above, load the incoming NAM before
        // replacing its old location with another resource-heavy algorithm.
        var modules=Enumerable.Range(0,11).OrderBy(m=>catalog.IsMx42&&catalog.Identity(m,after.GetEffect(m).Code) is var id&&id.EffectiveFamily==11&&!id.IsNone?0:1);
        foreach(int module in modules)
        {
            var a=before.GetEffect(module);var b=after.GetEffect(module);
            var algorithm=catalog.Find(module,b.Code);
            if(a.Code!=b.Code||!a.Parameters.Select(BitConverter.SingleToInt32Bits).SequenceEqual(b.Parameters.Select(BitConverter.SingleToInt32Bits)))catalog.RequireParameters(module,b.Code);
            void Select(uint code){byte[] value=new byte[8];value[0]=(byte)module;BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4),code);edits.Add(Frame(0x16,value));}
            if(a.Code!=b.Code)
            {
                if(catalog.IsMx42)
                {
                    var ai=catalog.Identity(module,a.Code);var bi=catalog.Identity(module,b.Code);
                    if(!ai.Known||!bi.Known)throw new NotSupportedException("Unknown MX42 identity cannot be edited live.");
                    if(!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix)&&module==5&&(ai.EffectiveFamily!=5||bi.EffectiveFamily!=5||bi.IsNone))throw new NotSupportedException("MX42 native USB selects internal slot 06 through the CAB-only decoder. Universal family/NONE changes here require a firmware protocol correction; no edit was sent.");
                    if(ai.EffectiveFamily!=bi.EffectiveFamily&&!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix))
                    {
                        // Native select short-circuits equal ordinals even after decode changes family.
                        // The existing virtual NONE control is the firmware's family selector.
                        uint none=catalog.ForFamily(module,ai.EffectiveFamily)[^1].Code;
                        if(!ai.IsNone)Select(none);
                        byte[] familyValue=new byte[12];familyValue[0]=(byte)module;
                        BinaryPrimitives.WriteUInt32LittleEndian(familyValue.AsSpan(4),none);
                        BinaryPrimitives.WriteSingleLittleEndian(familyValue.AsSpan(8),bi.EffectiveFamily);
                        edits.Add(Frame(5,familyValue));
                        if(bi.Ordinal!=0)Select(b.Code);
                    }
                    else Select(b.Code);
                }
                else Select(b.Code);
            }
            if(a.Enabled!=b.Enabled)edits.Add(Frame(0x15,[(byte)module,(byte)(b.Enabled?1:0),0,0]));
            // Sync/power setters reset bound controls in the native firmware.
            // Publish them first, then the requested dependent values.
            foreach(int slot in Enumerable.Range(0,15).OrderBy(s=>Catalog.ControlWord(catalog,module,s)?s switch{12=>2,14=>3,_=>4}:algorithm?.Parameters.FirstOrDefault(p=>p.Slot==s)?.Power==true?0:1))
                if(!(catalog.IsMx42&&module==10&&slot==12&&!Mx42Integration.Supports(Mx42Capability.SlotElevenParameter12))&&!(catalog.IsMx42&&module==10&&slot is 13 or 14&&Mx42Integration.Supports(Mx42Capability.Chain))&&(algorithm==null||algorithm.Parameters.Any(p=>p.Slot==slot)||Catalog.ControlWord(catalog,module,slot))&&(BitConverter.SingleToInt32Bits(a.Parameters[slot])!=BitConverter.SingleToInt32Bits(b.Parameters[slot])||a.Code!=b.Code&&!Catalog.ControlWord(catalog,module,slot)&&float.IsFinite(b.Parameters[slot])||catalog.IsMx42&&algorithm?.Parameters.Any(power=>power.Power&&a.Parameters[power.Slot]!=b.Parameters[power.Slot]&&algorithm.Parameters.Any(bound=>!bound.Power&&bound.Bind==power.Slot&&bound.Slot==slot))==true))
                {
                    if(!float.IsFinite(b.Parameters[slot]))throw new InvalidDataException(UiText.T("No se puede enviar un parámetro no finito."));
                    byte[] value=new byte[12];value[0]=(byte)module;value[1]=(byte)slot;
                    // Native 0x5675b0 leaves the two reserved bytes unassigned. We use zero.
                    BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(4),b.Code);
                    BinaryPrimitives.WriteSingleLittleEndian(value.AsSpan(8),b.Parameters[slot]);edits.Add(Frame(5,value));
                }
        }
        foreach(var (a,b) in left.Zip(right))
        {
            if(PresetComparison.RecordEquivalent(a,b)||a.Type==2)continue;
            if(a.Type==20)
            {
                if(!a.Value.AsSpan(0,1).SequenceEqual(b.Value.AsSpan(0,1))||!a.Value.AsSpan(2,2).SequenceEqual(b.Value.AsSpan(2,2)))throw new InvalidOperationException(UiText.T("Identificador o cabecera de bloque alterados."));
                continue;
            }
            if(a.Type==12)
            {
                if(a.Value[0]!=b.Value[0])throw new InvalidOperationException(UiText.T("El identificador EXP no se puede editar."));
                bool destination=!a.Value.AsSpan(1,3).SequenceEqual(b.Value.AsSpan(1,3));
                if(destination)
                {
                    ushort parameter=BinaryPrimitives.ReadUInt16LittleEndian(b.Value.AsSpan(2));if(parameter>14)throw new InvalidDataException(UiText.T("Parámetro EXP fuera de rango."));
                    edits.Add(Frame(13,[b.Value[0],b.Value[1],(byte)parameter,0]));
                }
                foreach(var (offset,endpoint) in new[]{(4,1),(8,0)})
                {
                    if(b.Value[1]==255)continue; // None canonicalizes endpoints to zero.
                    if(!destination&&a.Value.AsSpan(offset,4).SequenceEqual(b.Value.AsSpan(offset,4)))continue;
                    byte[] value=new byte[8];value[0]=(byte)endpoint;value[1]=b.Value[0];b.Value.AsSpan(offset,4).CopyTo(value.AsSpan(4));edits.Add(Frame(14,value));
                }
                continue;
            }
            if(a.Type is not (8 or 12 or 15 or 16))throw new InvalidOperationException(UiText.T($"No existe edición parcial para el tipo {a.Type:X4}."));
            edits.Add(Frame(b.Type,b.Value));
        }
        if(catalog.IsMx42&&Mx42Integration.Supports(Mx42Capability.Chain)&&Mx42ChainUsb.Requires(before,after))
        {
            // The acknowledged CHAIN bundle already publishes extended mode
            // and cuts. Keep a native order packet only when needed; mode1
            // remains the ordinary FX loop, which the bundle does not encode.
            bool orderChanged=!before.Chain.SequenceEqual(after.Chain);
            bool nativeCuts=after.FxLoopMode is 0 or 1&&(after.RoutingRaw[0]!=11||after.RoutingRaw[1]!=11);
            edits.RemoveAll(f=>Protocol.Records(Protocol.Decode(f).Payload).Any(r=>
                r.Type==6&&BinaryPrimitives.ReadUInt16LittleEndian(r.Value)==5&&after.FxLoopMode!=1||r.Type==8&&!orderChanged&&!nativeCuts));
        }
        return edits;
    }
    static byte[] Scalar(ushort selector,ushort value){byte[] b=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(b,selector);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2),value);return b;}
    internal static byte[] Frame(ushort type,byte[] value)
    {
        byte[] payload=new byte[12+value.Length];payload[2]=4;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8),type);BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10),(ushort)value.Length);value.CopyTo(payload,12);
        var frame=new List<byte>{0xf0,0x21,0x25,0x7e,0x47,0x50,0x2d,0x32,0x12,(byte)payload.Length,0,0,0};
        foreach(byte b in payload){frame.Add((byte)(b>>4));frame.Add((byte)(b&15));}frame.Add(0xf7);return frame.ToArray();
    }
    public async Task<PresetDocument> ApplyAsync(PresetDocument candidate)
    {
        using var context=session.Enter();await DeviceMidi.OperationGate.WaitAsync();try{return await ApplyCore(candidate);}finally{DeviceMidi.OperationGate.Release();}
    }
    internal async Task<PresetDocument> ApplyCore(PresetDocument candidate)
    {
        using var context=session.Enter();
        if(!IsConnected)throw new IOException(UiText.T("La sesión se ha interrumpido. Vuelve a conectar para leer el estado real; no se reenviarán cambios pendientes."));
        Mx42Policy.RequireTransition(Baseline,candidate);
        var edits=Build(Baseline,candidate);bool chainEdit=Mx42ChainUsb.Available&&Mx42ChainUsb.Requires(Baseline,candidate);if(edits.Count==0&&!chainEdit)return Baseline;
        var expected=ExpectedAfterAutoCab(Baseline,candidate,AutoCabinetMatch);
        var before=Baseline.SnapshotRecords();var sent=new List<DeviceResponse>();DeviceResponse? pre=null,post=null;string? error=null;
        string path=await EditorAuditLog.PrepareAsync("live",new{utc=DateTime.UtcNow,state="prepared",before,requested=candidate.SnapshotRecords(),expected=expected.SnapshotRecords(),planned=edits.Select(Convert.ToHexString)});
        try
        {
            pre=await session.Read(ReadQuery.ActivePreset);Check(pre);
            if(!Same(Baseline,await Normalize(pre)))throw new IOException(UiText.T("El preset de la pedalera ha cambiado. Vuelve a conectar para leer su estado actual; no se ha enviado la edición."));
            if(SharedCatalog.IsMx42&&Enumerable.Range(0,11).Any(m=>Baseline.GetEffect(m).Code!=candidate.GetEffect(m).Code))
            {
                var fresh=await session.Read(ReadQuery.State);Check(fresh);operatingSettings=new DeviceSettings(fresh.Records);AutoCabinetMatch=operatingSettings.Global(36)!=0;
                if(AutoCabinetMatch)throw new NotSupportedException("Disable Auto CAB Match in Settings before changing MX42 algorithms. Universal slots cannot assume a fixed AMP/CAB pair. No edit was sent.");
            }
            // Separate edits: firmware does not retain every repeated partial
            // type-5 TLV when algorithm+parameters share one logical message.
            using var activeOperation=Mx42Policy.ActiveOperation(Baseline.Index);
            if(chainEdit)await Mx42ChainUsb.Apply(Baseline,candidate);
            foreach(var edit in edits)
            {
                var records=Protocol.Records(Protocol.Decode(edit).Payload);
                var response=await session.Write(records);sent.Add(response);Check(response);
                if(records.Any(r=>r.Type==0x16)){if(SharedCatalog.IsMx42&&Mx42Integration.Supports(Mx42Capability.SceneSnapshot))await Mx42Integration.WaitForIdle();else await Task.Delay(80);}
                if(SharedCatalog.IsMx42)
                {
                    var selection=records.SingleOrDefault(r=>r.Type==0x16);
                    var family=records.SingleOrDefault(r=>r.Type==5&&r.Value[1]==0&&SharedCatalog.Identity(r.Value[0],BinaryPrimitives.ReadUInt32LittleEndian(r.Value.AsSpan(4))).IsNone);
                    if(selection!=null||family!=null)
                    {
                        int module=selection?.Value[0]??family!.Value[0];
                        uint wanted=selection!=null?BinaryPrimitives.ReadUInt32LittleEndian(selection.Value.AsSpan(4)):SharedCatalog.WireCode(module,(int)BinaryPrimitives.ReadSingleLittleEndian(family!.Value.AsSpan(8)),0);
                        var probe=await session.Read(ReadQuery.ActivePreset);Check(probe);var selected=PresetDocument.FromSnapshot(probe.Records,format);
                        if(selected.Index!=Baseline.Index||selected.GetEffect(module).Code!=wanted)throw new IOException("MX42 did not confirm the selected family/algorithm. Parameter writes stopped; no command repeated. Reconnect to read the actual state.");
                    }
                }
            }
            if(SharedCatalog.IsMx42&&Mx42Integration.Supports(Mx42Capability.SceneSnapshot)){if(edits.Count>0)await Mx42Integration.WaitForIdle();}
            else await Task.Delay(25);
            post=await session.Read(ReadQuery.ActivePreset);Check(post);
            var actual=await Normalize(post);
            if(!Same(expected,actual))throw new IOException(UiText.T("La lectura posterior no coincide con la edición solicitada. Se ha desconectado la edición en vivo. Vuelve a conectar para cargar el estado real."));
            Baseline=actual;return actual;
        }
        catch(Exception e){session.Reset();error=e.Message;throw;}
        finally{await EditorAuditLog.CompleteAsync(path,new{utc=DateTime.UtcNow,before,requested=candidate.SnapshotRecords(),expected=expected.SnapshotRecords(),autoCabinetMatch=AutoCabinetMatch,planned=edits.Select(Convert.ToHexString),sent,pre,post,error});}
    }
}
