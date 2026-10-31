using System.Buffers.Binary;
namespace Gp200.Replica;

/// <summary>Exact foreground bank snapshot. A BASE context is independent of bank presence.</summary>
public sealed class Mx42SceneSnapshot
{
    public const int Size=SceneLayout.Size;
    readonly byte[] data;
    public uint Revision=>U(SceneLayout.Revision);
    public int Slot=>(int)U(SceneLayout.Slot);
    public int Mode=>(int)U(SceneLayout.Mode);
    public bool HasScenes=>U(SceneLayout.Presence)==15;
    public int Active=>(int)U(SceneLayout.Active);
    public bool HasBase=>U(SceneLayout.HasBase)==1;
    uint U(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
    static void Put(byte[] a,int at,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(at),v);
    public byte[] ToBytes()=>(byte[])data.Clone();
    public Mx42SceneSnapshot(byte[] bytes)
    {
        if(bytes.Length!=Size)throw new InvalidDataException("Invalid MX42 Scene snapshot size.");data=(byte[])bytes.Clone();
        if(U(SceneLayout.MagicAt)!=SceneLayout.Magic||U(SceneLayout.VersionAt)!=SceneLayout.Version||U(SceneLayout.SizeAt)!=Size||U(SceneLayout.Revision)!=Crc(data)||U(SceneLayout.Slot)>255||U(SceneLayout.Mode)>3||U(SceneLayout.Presence) is not(0 or 15)||U(SceneLayout.Active) is not(0 or 1 or 2 or 3 or 255)||U(SceneLayout.HasBase)>1||data.AsSpan(SceneLayout.Reserved,SceneLayout.ReservedBytes).ContainsAnyExcept((byte)0)||U(SceneLayout.Slot)!=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(SceneLayout.Header)))throw new InvalidDataException("Invalid MX42 Scene snapshot header or CRC.");
        if(!HasScenes&&Active!=255)throw new InvalidDataException("An absent Scene bank cannot have an active Scene.");
        for(int b=0;b<11;b++)if(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(SceneLayout.Ownership+2*b))>0x7fff)throw new InvalidDataException("Invalid Scene ownership mask.");
        foreach(int off in SceneLayout.AllStates)if((U(off+SceneLayout.BypassInState)&~0x7ffu)!=0)throw new InvalidDataException("Invalid Scene bypass mask.");
    }
    internal static uint Crc(ReadOnlySpan<byte> p)
    {uint c=uint.MaxValue;for(int i=0;i<p.Length;i++){c^=i>=SceneLayout.Revision&&i<SceneLayout.Revision+sizeof(uint)?(byte)0:p[i];for(int b=0;b<8;b++)c=(c>>1)^((0u-(c&1))&0xedb88320);}return ~c;}
    Mx42SceneSnapshot Changed(Action<byte[]> edit){var p=ToBytes();edit(p);Put(p,SceneLayout.Revision,Crc(p));return new(p);}
    internal Mx42SceneSnapshot WithBaseFromLive()=>Changed(p=>{Array.Copy(p,SceneLayout.LiveWords,p,SceneLayout.BaseWords,SceneLayout.SceneBytes);Put(p,SceneLayout.HasBase,1);});
    bool SameLive(Mx42SceneSnapshot other)
    {if(U(SceneLayout.LiveBypass)!=other.U(SceneLayout.LiveBypass))return false;for(int m=0;m<11;m++){int mask=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(SceneLayout.Ownership+2*m));for(int p=0;p<15;p++)if((mask&(1<<p))!=0){int off=SceneLayout.LiveWords+4*(m*15+p);if(U(off)!=other.U(off))return false;}}return true;}
    internal Mx42SceneSnapshot AtBase()=>Changed(p=>{if(!HasBase)throw new InvalidDataException("Saved BASE is unavailable for this topology.");Array.Copy(p,SceneLayout.BaseWords,p,SceneLayout.LiveWords,SceneLayout.SceneBytes);Put(p,SceneLayout.Active,255);});
    internal Mx42SceneSnapshot WithoutScenes()=>Changed(p=>{Array.Clear(p,SceneLayout.FirstScene,SceneLayout.AllScenesBytes);Put(p,SceneLayout.Presence,0);Put(p,SceneLayout.Active,255);});
    internal Mx42SceneSnapshot WithLive(PresetDocument document,int active)=>Changed(p=>
    {for(int m=0;m<11;m++)for(int i=0;i<15;i++)Put(p,SceneLayout.LiveWords+4*(15*m+i),BitConverter.SingleToUInt32Bits(document.GetEffect(m).Parameters[i]));uint on=0;for(int m=0;m<11;m++)if(document.GetEffect(m).Enabled)on|=1u<<m;Put(p,SceneLayout.LiveBypass,on);Put(p,SceneLayout.Active,(uint)active);});
    internal PresetDocument ApplySceneWords(PresetDocument template,int offset)
    {
        // Archive reconstruction, not an interactive setter: preserve every raw
        // unused word/NaN payload. Bundle validation checks owned words separately.
        byte[] bytes=template.ToBytes();uint on=U(offset+SceneLayout.BypassInState);
        foreach(var record in template.Records.Where(r=>r.Type==20))
        {int module=bytes[record.Offset+4];bytes[record.Offset+5]=(on&(1u<<module))!=0?(byte)1:(byte)0;data.AsSpan(offset+60*module,60).CopyTo(bytes.AsSpan(record.Offset+12));}
        uint checksum=0;foreach(byte value in bytes.AsSpan(0,bytes.Length-4))checksum=unchecked(checksum+value);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length-4),checksum);return new(bytes);
    }
    internal bool SamePersistentBank(Mx42SceneSnapshot other)
    {
        if(HasScenes!=other.HasScenes||HasBase!=other.HasBase||!data.AsSpan(SceneLayout.Identity,SceneLayout.IdentityBytes).SequenceEqual(other.data.AsSpan(SceneLayout.Identity,SceneLayout.IdentityBytes)))return false;
        if(HasBase&&!data.AsSpan(SceneLayout.BaseWords,SceneLayout.SceneBytes).SequenceEqual(other.data.AsSpan(SceneLayout.BaseWords,SceneLayout.SceneBytes)))return false;
        if(!HasScenes)return true;
        for(int a=0;a<4;a++)for(int m=0;m<11;m++)
        {int mask=BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(SceneLayout.Ownership+2*m));for(int p=0;p<15;p++)if((mask&(1<<p))!=0){int off=SceneLayout.SceneAt(a)+4*(m*15+p);if(!data.AsSpan(off,4).SequenceEqual(other.data.AsSpan(off,4)))return false;}}
        for(int a=0;a<4;a++)if(U(SceneLayout.SceneAt(a)+660)!=other.U(SceneLayout.SceneAt(a)+660))return false;
        return true;
    }
    internal bool SameBank(Mx42SceneSnapshot other,bool live=true)=>HasScenes==other.HasScenes&&HasBase==other.HasBase&&Active==other.Active&&data.AsSpan(SceneLayout.Identity,SceneLayout.IdentityBytes).SequenceEqual(other.data.AsSpan(SceneLayout.Identity,SceneLayout.IdentityBytes))&&(!HasScenes||data.AsSpan(SceneLayout.FirstScene,SceneLayout.AllScenesBytes).SequenceEqual(other.data.AsSpan(SceneLayout.FirstScene,SceneLayout.AllScenesBytes)))&&(!HasBase||data.AsSpan(SceneLayout.BaseWords,SceneLayout.SceneBytes).SequenceEqual(other.data.AsSpan(SceneLayout.BaseWords,SceneLayout.SceneBytes)))&&(!live||SameLive(other));
}

internal static class Mx42SceneUsb
{
    internal static async Task<Mx42SceneSnapshot> Read()
    {
        if(!Mx42Integration.Supports(Mx42Capability.SceneSnapshot))throw new NotSupportedException("Firmware does not expose the complete Scene snapshot service.");
        var begin=await Mx42Integration.Request(2,0);Check(begin);if(begin.Arg0!=Mx42SceneSnapshot.Size||begin.Arg1==0)throw new InvalidDataException("Unexpected Scene snapshot session.");
        var bytes=new byte[Mx42SceneSnapshot.Size];uint token=begin.Arg1;
        for(int offset=0;offset<bytes.Length;offset+=Mx42Integration.MaxChunk){var r=await Mx42Integration.Request(2,1,begin.Revision,(uint)offset,token);Check(r);if(r.Revision!=begin.Revision||r.Arg0!=offset||r.Payload.Length!=Math.Min(Mx42Integration.MaxChunk,bytes.Length-offset))throw new IOException("Scene snapshot chunk or revision differs; no retry was sent.");r.Payload.CopyTo(bytes,offset);}
        var snapshot=new Mx42SceneSnapshot(bytes);if(snapshot.Revision!=begin.Revision)throw new IOException("Scene snapshot CRC differs from its session.");return snapshot;
    }
    internal static async Task<Mx42SceneSnapshot> Restore(Mx42SceneSnapshot snapshot,uint revision)
    {
        if(!Mx42Integration.Supports(Mx42Capability.SceneRestore))throw new NotSupportedException("Firmware does not expose complete Scene restore.");
        var bytes=snapshot.ToBytes();var begin=await Mx42Integration.Request(2,2,revision,(uint)bytes.Length);Check(begin);uint token=begin.Arg1;if(token==0)throw new IOException("Missing Scene restore session token.");
        for(int offset=0;offset<bytes.Length;offset+=Mx42Integration.MaxChunk)Check(await Mx42Integration.Request(2,3,revision,(uint)offset,token,bytes.AsSpan(offset,Math.Min(Mx42Integration.MaxChunk,bytes.Length-offset)).ToArray()));
        Check(await Mx42Integration.Request(2,4,revision,0,token));var actual=await Read();if(!snapshot.SameBank(actual))throw new IOException("Scene restore readback differs. No replay or automatic rollback was sent.");return actual;
    }
    internal static async Task Base(uint revision)
    {if(!Mx42Integration.Supports(Mx42Capability.SceneBase))throw new NotSupportedException("Firmware does not expose explicit BASE recall.");Check(await Mx42Integration.Request(2,5,revision));}
    static void Check(Mx42Reply r){if(r.Status!=0)throw new IOException($"MX42 Scene service rejected the operation ({r.Status}). No automatic retry was sent.");}
}
