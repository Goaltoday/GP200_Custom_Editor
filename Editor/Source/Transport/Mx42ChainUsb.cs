using System.Buffers.Binary;
namespace Gp200.Replica;

internal sealed record Mx42ChainState(bool Enabled,Mx42ChainConfig Config,uint Revision);
internal static class Mx42ChainUsb
{
    internal const uint Capability=8;
    internal static bool Available=>CatalogRuntime.Current.IsMx42&&Mx42Integration.Supports(Capability);
    internal static uint Crc(ReadOnlySpan<byte> bytes)=>Crc32.Compute(bytes);
    internal static byte[] Encode(bool enabled,Mx42ChainConfig c)
    {
        if(!c.Valid)throw new InvalidDataException("Invalid CHAIN configuration.");
        byte[] b=new byte[32];uint[] values=[enabled?1u:0u,(uint)c.Start,(uint)c.Split,(uint)c.End,BitConverter.SingleToUInt32Bits(c.Blend),unchecked((uint)c.Align),c.Inverted?1u:0u];
        for(int i=0;i<7;i++)BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i*4),values[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28),Crc(b.AsSpan(0,28)));return b;
    }
    internal static Mx42ChainState Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length!=32)throw new InvalidDataException("Invalid CHAIN reply length.");
        return DecodeArray(bytes.ToArray());
    }
    static Mx42ChainState DecodeArray(byte[] b)
    {
        uint U(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at,4));
        if(U(0)>1||U(24)>1||Crc(b.AsSpan(0,28))!=U(28))throw new InvalidDataException("Invalid CHAIN reply values or CRC.");
        var c=new Mx42ChainConfig(checked((int)U(4)),checked((int)U(8)),checked((int)U(12)),unchecked((int)U(20)),U(24)!=0,BitConverter.UInt32BitsToSingle(U(16)));
        if(!c.Valid)throw new InvalidDataException("Invalid effective CHAIN configuration.");return new(U(0)!=0,c,U(28));
    }
    static Mx42ChainState Checked(Mx42Reply reply)
    {
        if(reply.Status!=0)throw new IOException($"CHAIN service rejected the operation (status {reply.Status}). No command was repeated.");
        var state=Decode(reply.Payload);if(state.Revision!=reply.Revision)throw new InvalidDataException("CHAIN revision differs from its state CRC.");return state;
    }
    internal static Task<Mx42ChainState> Read()=>ReadCore();
    static async Task<Mx42ChainState> ReadCore()
    {
        if(!Available)throw new NotSupportedException("The connected firmware does not advertise the CHAIN USB service.");
        return Checked(await Mx42Integration.Request(1,0));
    }
    internal static bool HasSavedConfig(PresetDocument document)
    {try{_=Mx42ChainConfig.Read(document);return true;}catch(InvalidDataException){return false;}}
    internal static bool Requires(PresetDocument before,PresetDocument after)
    {
        // Native FX-loop mode0/1 and Send/Return remain the existing native path.
        if(!ChainRouting.Extended(before.FxLoopMode)&&!ChainRouting.Extended(after.FxLoopMode))return false;
        return before.FxLoopMode!=after.FxLoopMode||!before.RoutingRaw.SequenceEqual(after.RoutingRaw)||Mx42ChainConfig.GetOrDefault(before)!=Mx42ChainConfig.GetOrDefault(after);
    }
    internal static void Normalize(PresetDocument document,Mx42ChainState state)
    {
        // A never-enabled preset can contain unrelated/uninitialized metadata.
        // A read must not manufacture an edited preset by adding that metadata.
        if(state.Enabled||HasSavedConfig(document))Project(document,state);
    }
    internal static void Project(PresetDocument document,Mx42ChainState state)
    {
        var c=state.Config;
        // A disabled CHAIN GET does not describe the native FX-loop route.
        // Preserve native mode0/1 and its Send/Return while normalizing metadata.
        if(state.Enabled)document.SetCustomRouting(0x80|c.Split,c.Start,c.Split,c.End);
        else if(document.FxLoopMode is not (0 or 1))document.SetCustomRouting(0,11,11,11);
        c.Write(document); // Disabled routing still retains its saved A/B cuts.
    }
    // Caller owns OperationGate, the active-preset preflight and final preset readback.
    internal static async Task<Mx42ChainState> Apply(PresetDocument before,PresetDocument after)
    {
        var old=await Read();var prior=Mx42ChainConfig.GetOrDefault(before);
        if(old.Enabled!=ChainRouting.Enabled(before.FxLoopMode)||(HasSavedConfig(before)||old.Enabled)&&old.Config!=prior)throw new IOException("Effective CHAIN changed before the edit. No edit was sent.");
        bool enabled=ChainRouting.Enabled(after.FxLoopMode);var desired=Mx42ChainConfig.GetOrDefault(after);
        var actual=Checked(await Mx42Integration.Request(1,1,old.Revision,payload:Encode(enabled,desired)));
        if(actual.Enabled!=enabled||actual.Config!=desired)throw new IOException("CHAIN readback differs. No command was repeated.");
        Project(after,actual);return actual;
    }
}
