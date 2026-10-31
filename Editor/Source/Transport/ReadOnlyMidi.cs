using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Gp200.Replica;

public sealed record DeviceResponse(ReadQuery Query,int RequestedSlot,string Transmitted,IReadOnlyList<string> Received,
    IReadOnlyList<WireRecord> Records,int Length,IReadOnlyList<string> CleanupErrors)
{
    public IReadOnlyList<WireRecord>? WireRecords {get;init;}
    public int? VerifiedActiveSlot {get;init;}
}

/// <summary>Four public typed reads plus an internal restricted active-preset edit path.
/// No MIDI reset, program change, persistent save or firmware packets.</summary>
public static class ReadOnlyMidi
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct InCaps{public ushort Manufacturer,Product;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public uint Support;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct OutCaps{public ushort Manufacturer,Product;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public ushort Technology,Voices,Notes,ChannelMask;public uint Support;}
    [StructLayout(LayoutKind.Sequential)]
    struct Header{public IntPtr Data;public uint Length,Recorded;public UIntPtr User;public uint Flags;public IntPtr Next;public UIntPtr Reserved;public uint Offset;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public UIntPtr[] ReservedArray;}
    delegate void Callback(IntPtr handle,uint message,UIntPtr instance,UIntPtr parameter,UIntPtr timestamp);
    const string Library="winmm.dll";
    [DllImport(Library)]static extern uint midiInGetNumDevs();
    [DllImport(Library)]static extern uint midiOutGetNumDevs();
    [DllImport(Library,CharSet=CharSet.Unicode)]static extern uint midiInGetDevCapsW(UIntPtr id,out InCaps caps,uint size);
    [DllImport(Library,CharSet=CharSet.Unicode)]static extern uint midiOutGetDevCapsW(UIntPtr id,out OutCaps caps,uint size);
    [DllImport(Library)]static extern uint midiInOpen(out IntPtr handle,uint id,Callback callback,UIntPtr instance,uint flags);
    [DllImport(Library)]static extern uint midiOutOpen(out IntPtr handle,uint id,UIntPtr callback,UIntPtr instance,uint flags);
    [DllImport(Library)]static extern uint midiInPrepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInUnprepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInAddBuffer(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiInStart(IntPtr h);
    [DllImport(Library)]static extern uint midiInStop(IntPtr h);
    [DllImport(Library)]static extern uint midiInReset(IntPtr h);
    [DllImport(Library)]static extern uint midiInClose(IntPtr h);
    [DllImport(Library)]static extern uint midiOutPrepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutUnprepareHeader(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutLongMsg(IntPtr h,IntPtr header,uint size);
    [DllImport(Library)]static extern uint midiOutClose(IntPtr h);
    static readonly SemaphoreSlim Gate=new(1,1);
    // Retain callback and allocations if a faulty driver refuses to release them.
    static readonly ConcurrentBag<object> Quarantine=new();
    static uint HeaderSize=checked((uint)Marshal.SizeOf<Header>());

    public static async Task<DeviceResponse> QueryAsync(ReadQuery query,int slot=0)
    {
        byte[] request=Protocol.ReadRequest(query,slot);
        await Gate.WaitAsync();try{return await Task.Run(()=>
        {
            if(query!=ReadQuery.ActivePreset)return Query(query,slot,request,match:records=>Matches(query,slot,records));
            if(Mx42Integration.Supports(Mx42Capability.CoherentActive))
            {
                // Capability belongs only to the reviewed foreground coherent-copy hook.
                var active=Query(query,slot,request,match:records=>Matches(query,slot,records));
                int actual=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(active.Records.Single(r=>r.Type==2).Value);
                Mx42Policy.ObserveSnapshot(active.Records);return ResolveActivePosition(active,actual);
            }
            // The loaded preset can retain a stale internal slot in its general
            // and chain headers. The global CurrentPreset is the real position.
            // Bracket the read so a pedal press cannot attach another body to it.
            for(int attempt=0;attempt<3;attempt++)
            {
                DeviceResponse State()=>Query(ReadQuery.State,0,Protocol.ReadRequest(ReadQuery.State),match:r=>Matches(ReadQuery.State,0,r));
                int Position(DeviceResponse state)=>GlobalSnapshot.Parse(state.Records.Single(r=>r.Type==0x1007).Value).CurrentPreset;
                var before=State();var active=Query(query,slot,request,match:records=>Matches(query,slot,records));var after=State();
                if(Position(before)!=Position(after))continue;
                Mx42Policy.ObserveSnapshot(active.Records);return ResolveActivePosition(active,Position(after)) with{CleanupErrors=before.CleanupErrors.Concat(active.CleanupErrors).Concat(after.CleanupErrors).ToArray()};
            }
            throw new IOException("The active preset changed during the read. No edit was sent.");
        });}finally{Gate.Release();}
    }
    internal static DeviceResponse ResolveActivePosition(DeviceResponse response,int actualSlot)
    {
        if(actualSlot is <0 or >255)throw new InvalidDataException("Active preset position is outside the GP-200 library.");
        var records=response.Records.Select(r=>new WireRecord(r.Type,(byte[])r.Value.Clone())).ToArray();
        foreach(var r in records)
        {
            if(r.Type==0&&r.Value.Length==4)System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(r.Value.AsSpan(2),(ushort)actualSlot);
            if(r.Type==2&&r.Value.Length==88||r.Type==8&&r.Value.Length==16)System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(r.Value,(ushort)actualSlot);
        }
        return response with{Records=records,WireRecords=response.WireRecords??response.Records,VerifiedActiveSlot=actualSlot};
    }
    static bool Matches(ReadQuery query,int slot,IReadOnlyList<WireRecord> records)
    {
        if(query==ReadQuery.Identity)return records.Count==1&&records[0].Type==0x2001&&records[0].Value.Length==4;
        if(query==ReadQuery.State)return records.Any(r=>r.Type==0x1007&&r.Value.Length==72)&&records.Any(r=>r.Type==0x1004&&r.Value.Length==124);
        var context=records.SingleOrDefault(r=>r.Type==0&&r.Value.Length==4);var general=records.SingleOrDefault(r=>r.Type==2&&r.Value.Length==88);
        if(context==null||general==null||records.Count(r=>r.Type==20&&r.Value.Length==68)!=11)return false;
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(context.Value)==(query==ReadQuery.ActivePreset?0:1)&&
            (query!=ReadQuery.SavedPreset||System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(general.Value)==slot);
    }
    internal static async Task<DeviceResponse> ExchangeInternalAsync(byte command,IReadOnlyList<WireRecord> records,Func<IReadOnlyList<WireRecord>,bool>? match,int timeoutMs)
    {
        Mx42Policy.CheckWire(command,records);var frames=Protocol.Encode(command,records);await Gate.WaitAsync();
        bool integration=command==0x11&&records.Count==1&&records[0].Type==0x1009&&records[0].Value.Length>=32&&System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(records[0].Value)==0x584d&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(records[0].Value.AsSpan(4))==0x3145584d;
        try{return await Task.Run(()=>Query(ReadQuery.State,0,frames[0],match==null,frames,match,true,timeoutMs,integrationPacing:integration));}finally{Gate.Release();}
    }
    static void Check(uint result,string operation){if(result!=0)throw new IOException(UiText.T($"{operation}: error MIDI {result}. Cierra el editor oficial y otras aplicaciones que usen GP-200."));}
    public static bool IsDeviceAvailable()
    {
        try{_=FindInput();_=FindOutput();return true;}catch(IOException){return false;}
    }
    static uint FindInput()
    {
        var matches=new List<uint>();for(uint i=0;i<midiInGetNumDevs();i++){Check(midiInGetDevCapsW((UIntPtr)i,out var caps,(uint)Marshal.SizeOf<InCaps>()),UiText.T("Enumerar entrada"));if(caps.Name=="GP-200")matches.Add(i);}
        return matches.Count==1?matches[0]:throw new IOException(UiText.T("Se necesita una única entrada MIDI con nombre GP-200."));
    }
    static uint FindOutput()
    {
        var matches=new List<uint>();for(uint i=0;i<midiOutGetNumDevs();i++){Check(midiOutGetDevCapsW((UIntPtr)i,out var caps,(uint)Marshal.SizeOf<OutCaps>()),UiText.T("Enumerar salida"));if(caps.Name=="GP-200")matches.Add(i);}
        return matches.Count==1?matches[0]:throw new IOException(UiText.T("Se necesita una única salida MIDI con nombre GP-200."));
    }
    static DeviceResponse Query(ReadQuery query,int slot,byte[] request,bool edit=false,IReadOnlyList<byte[]>? fragments=null,Func<IReadOnlyList<WireRecord>,bool>? match=null,bool generic=false,int timeoutMs=3000,bool integrationPacing=false)
    {
        var received=new ConcurrentQueue<byte[]>();var callbackErrors=new ConcurrentQueue<string>();
        Callback callback=(handle,message,instance,parameter,time)=>
        {
            if(message is not (0x3c4 or 0x3c6))return;
            try{var header=Marshal.PtrToStructure<Header>((IntPtr)parameter);if(header.Recorded==0)return;if(header.Recorded>header.Length){callbackErrors.Enqueue(UiText.T("El driver indicó datos fuera del buffer."));return;}var bytes=new byte[header.Recorded];Marshal.Copy(header.Data,bytes,0,bytes.Length);if(message==0x3c6)callbackErrors.Enqueue(UiText.T("Driver MIDI: error de recepción SysEx."));received.Enqueue(bytes);}
            catch(Exception e){callbackErrors.Enqueue(e.Message);}
        };
        var buffers=new List<(IntPtr Data,IntPtr Header,bool Prepared)>();var cleanup=new List<string>();
        IntPtr input=IntPtr.Zero,output=IntPtr.Zero,outData=IntPtr.Zero,outHeader=IntPtr.Zero;bool outPrepared=false;
        try
        {
            uint inputId=FindInput(),outputId=FindOutput();
            Check(midiInOpen(out input,inputId,callback,UIntPtr.Zero,0x30000),UiText.T("Abrir entrada"));
            for(int n=0;n<32;n++)
            {
                IntPtr data=Marshal.AllocHGlobal(16384),header=Marshal.AllocHGlobal((int)HeaderSize);buffers.Add((data,header,false));
                Marshal.StructureToPtr(new Header{Data=data,Length=16384,ReservedArray=new UIntPtr[8]},header,false);
                Check(midiInPrepareHeader(input,header,HeaderSize),UiText.T("Preparar recepción"));buffers[^1]=(data,header,true);Check(midiInAddBuffer(input,header,HeaderSize),UiText.T("Encolar recepción"));
            }
            Check(midiInStart(input),UiText.T("Iniciar recepción"));Check(midiOutOpen(out output,outputId,UIntPtr.Zero,UIntPtr.Zero,0),UiText.T("Abrir salida"));
            int packet=0;foreach(var frame in fragments??new[]{request})
            {
                outData=Marshal.AllocHGlobal(frame.Length);Marshal.Copy(frame,0,outData,frame.Length);outHeader=Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new Header{Data=outData,Length=(uint)frame.Length,ReservedArray=new UIntPtr[8]},outHeader,false);
                Check(midiOutPrepareHeader(output,outHeader,HeaderSize),UiText.T("Preparar mensaje"));outPrepared=true;Check(midiOutLongMsg(output,outHeader,HeaderSize),UiText.T("Enviar mensaje"));
                var tx=Stopwatch.StartNew();while((Marshal.PtrToStructure<Header>(outHeader).Flags&1)==0&&tx.ElapsedMilliseconds<3000)Thread.Sleep(2);
                if((Marshal.PtrToStructure<Header>(outHeader).Flags&1)==0)throw new IOException(UiText.T("El driver no confirmó el envío."));
                Check(midiOutUnprepareHeader(output,outHeader,HeaderSize),UiText.T("Liberar envío"));outPrepared=false;Marshal.FreeHGlobal(outHeader);Marshal.FreeHGlobal(outData);outHeader=outData=IntPtr.Zero;
                packet++;if(fragments?.Count>1)Thread.Sleep(5);
            }
            if(!integrationPacing&&fragments?.Count>1)Thread.Sleep(1000); // Native host transfer pacing, 0x5ae6d0.
            // Local bounded timeout; not claimed as a firmware timing requirement.
            var deadline=Stopwatch.StartNew();bool complete=false;
            while(deadline.ElapsedMilliseconds<timeoutMs)
            {
                Thread.Sleep(5);
                complete=CompletedMessages(received.ToArray()).Any(message=>match==null||match(Protocol.Records(Protocol.Reassemble(message))));
                if(complete||edit){if(edit)Thread.Sleep(5);break;}
            }
        }
        finally
        {
            void Note(string operation,uint result){if(result!=0)cleanup.Add($"{operation}: {result}");}
            if(input!=IntPtr.Zero){Note("stop input",midiInStop(input));Note("return input buffers",midiInReset(input));}
            foreach(var buffer in buffers)
            {
                uint result=buffer.Prepared?midiInUnprepareHeader(input,buffer.Header,HeaderSize):0;
                if(result==0){Marshal.FreeHGlobal(buffer.Header);Marshal.FreeHGlobal(buffer.Data);}else{Note("release input buffer",result);Quarantine.Add(buffer);Quarantine.Add(callback);}
            }
            if(input!=IntPtr.Zero){uint result=midiInClose(input);Note("close input",result);if(result!=0)Quarantine.Add(callback);}
            uint outResult=outPrepared?midiOutUnprepareHeader(output,outHeader,HeaderSize):0;
            if(outResult==0){if(outHeader!=IntPtr.Zero)Marshal.FreeHGlobal(outHeader);if(outData!=IntPtr.Zero)Marshal.FreeHGlobal(outData);}else{Note("release output buffer",outResult);Quarantine.Add((outData,outHeader));}
            if(output!=IntPtr.Zero)Note("close output",midiOutClose(output));
            GC.KeepAlive(callback);
        }
        if(callbackErrors.TryPeek(out var error))throw new IOException(error);
        byte[][] frames=received.ToArray();
        string transmitted=string.Join(UiText.T(" "),(fragments??new[]{request}).Select(Convert.ToHexString));
        if(edit)return new(query,slot,transmitted,frames.Select(Convert.ToHexString).ToArray(),[],0,cleanup);
        var selected=CompletedMessages(frames).LastOrDefault(message=>match==null||match(Protocol.Records(Protocol.Reassemble(message))))??throw new IOException(UiText.T("No llegó la respuesta esperada de GP-200."));
        byte[] payload=Protocol.Reassemble(selected);
        var records=Protocol.Records(payload);
        if(generic)return new(query,slot,transmitted,frames.Select(Convert.ToHexString).ToArray(),records,payload.Length,cleanup);
        // A response is identified by its logical content, never by length alone.
        if(query==ReadQuery.Identity&&(records.Count!=1||records[0].Type!=0x2001||records[0].Value.Length!=4))throw new InvalidDataException(UiText.T("La respuesta no corresponde a identidad."));
        if(query==ReadQuery.State&&!records.Any(r=>r.Type==0x1007))throw new InvalidDataException(UiText.T("La respuesta no contiene estado global."));
        if(query is ReadQuery.ActivePreset or ReadQuery.SavedPreset)
        {
            var context=records.Where(r=>r.Type==0&&r.Value.Length==4).ToArray();
            var general=records.Where(r=>r.Type==2&&r.Value.Length>=88).ToArray();
            if(context.Length!=1||general.Length!=1)throw new InvalidDataException(UiText.T("Respuesta de preset ambigua."));
            ushort mode=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(context[0].Value);
            if(mode!=(query==ReadQuery.ActivePreset?0:1))throw new InvalidDataException(UiText.T("Contexto de preset distinto del solicitado."));
            if(query==ReadQuery.SavedPreset&&System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(general[0].Value)!=slot)throw new InvalidDataException(UiText.T("La respuesta pertenece a otro preset guardado."));
        }
        return new(query,slot,Convert.ToHexString(request),frames.Select(Convert.ToHexString).ToArray(),records,payload.Length,cleanup);
    }
    internal static List<byte[][]> CompletedMessages(IEnumerable<byte[]> chunks)
    {
        var completed=new List<byte[][]>();var frame=new List<byte>();var pending=new List<byte[]>();int expected=0;bool inside=false;
        foreach(byte value in chunks.SelectMany(c=>c))
        {
            if(value>=0xf8)continue; // MIDI realtime may be interleaved inside SysEx.
            if(value==0xf0){frame.Clear();frame.Add(value);inside=true;continue;}
            if(!inside)continue;frame.Add(value);if(value!=0xf7)continue;inside=false;
            try
            {
                byte[] bytes=frame.ToArray();var part=Protocol.Decode(bytes);
                if(part.Offset==0){pending.Clear();expected=0;}
                if(part.Offset!=expected){pending.Clear();expected=0;continue;}
                pending.Add(bytes);expected+=part.Payload.Length;
                if(expected==part.Total){_=Protocol.Reassemble(pending);completed.Add(pending.ToArray());pending.Clear();expected=0;}
            }
            catch(InvalidDataException){pending.Clear();expected=0;}
        }
        return completed;
    }
}
