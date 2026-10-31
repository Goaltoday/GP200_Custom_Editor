using System.Buffers.Binary;
namespace Gp200.Replica;
internal static class Mx42Policy
{
    static readonly AsyncLocal<int?> activeIndex=new();
    static readonly AsyncLocal<int?> replacementIndex=new();
    internal static IDisposable ReplacementOperation(int index)=>new ReplacementScope(index);
    sealed class ReplacementScope:IDisposable
    {readonly int? prior=replacementIndex.Value;internal ReplacementScope(int index){if(index is <0 or >255)throw new ArgumentOutOfRangeException(nameof(index));prior=replacementIndex.Value;replacementIndex.Value=index;}public void Dispose()=>replacementIndex.Value=prior;}

    internal static bool UniversalObserved {get=>DeviceSession.Current.UniversalObserved;private set=>DeviceSession.Current.UniversalObserved=value;}
    internal static IDisposable ActiveOperation(int index)=>new ActiveScope(index);
    sealed class ActiveScope:IDisposable
    {readonly int? prior=activeIndex.Value;internal ActiveScope(int index){activeIndex.Value=index;}public void Dispose()=>activeIndex.Value=prior;}
    internal static void ObserveSnapshot(IReadOnlyList<WireRecord> records)
    {if(records.Where(r=>r.Type==20&&r.Value.Length==68).Any(r=>BinaryPrimitives.ReadUInt32LittleEndian(r.Value.AsSpan(4))>>24 is >=0xe5 and <=0xea))UniversalObserved=true;}
    internal const string Limitation="This MX42 operation has no verified lossless USB path. No command was sent. See PROTOCOL_GAPS.md.";
    internal static void RequireTransition(PresetDocument before,PresetDocument after)
    {
        if(!CatalogRuntime.Current.IsMx42)return;
        Mx42NamLimit.Validate(after);
        if(before.Index!=after.Index)throw new InvalidOperationException("Live edits cannot change preset position.");
        var a=before.GetEffect(10);var b=after.GetEffect(10);
        if(!Mx42Integration.Supports(Mx42Capability.SlotElevenParameter12)&&BitConverter.SingleToUInt32Bits(a.Parameters[12])!=BitConverter.SingleToUInt32Bits(b.Parameters[12])&&a.Code==b.Code)
            throw new NotSupportedException("MX42's legacy USB ingress reserves physical slot 11 parameter 12. This DSP control is readable but cannot be changed through that receiver. No edit was sent.");
        if(!Mx42Integration.Supports(Mx42Capability.Chain)&&!a.Parameters.Skip(13).Select(BitConverter.SingleToUInt32Bits).SequenceEqual(b.Parameters.Skip(13).Select(BitConverter.SingleToUInt32Bits)))
            throw new NotSupportedException("MX42's native parameter receiver does not publish CHAIN words 13/14 to the live routing engine. BLEND, ALIGN and topology edits remain local until a firmware protocol extension is provided. No edit was sent.");
        for(int slot=0;slot<11;slot++)
        {
            var x=before.GetEffect(slot);var y=after.GetEffect(slot);var c=CatalogRuntime.Current;
            if(!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix)&&x.Code!=y.Code&&slot==5&&(c.Identity(slot,x.Code).EffectiveFamily!=5||c.Identity(slot,y.Code).EffectiveFamily!=5||c.Identity(slot,y.Code).IsNone))
                throw new NotSupportedException("Internal slot 06 uses a CAB-only USB selection decoder in MX42. Universal family/NONE changes there require a firmware correction; no edit was sent.");
        }
    }
    internal static void CheckWire(byte command,IReadOnlyList<WireRecord> records)
    {
        var catalog=CatalogRuntime.Current;if(!catalog.IsMx42&&!UniversalObserved)return;
        if(!catalog.IsMx42)throw new NotSupportedException("Universal effect IDs were observed. Select the MX42 profile before sending edits.");
        if(command==0x11)
        {
            if(records.Any(r=>r.Type==0x1009&&r.Value.Length>=8&&BinaryPrimitives.ReadUInt32LittleEndian(r.Value.AsSpan(4)) is 0x31304344 or 0x31304e41))throw new NotSupportedException("DC01/AN01 are Custom5 services, not MX42 discovery. Use the explicit MX42 profile and local inventory.");
            return; // SFU1 GET/SET remains its native revision-checked protocol; no new command.
        }
        if(command!=0x12)throw new NotSupportedException(Limitation);
        if(records.Any(r=>r.Type==2))
        {
            if(!Mx42Integration.Supports(Mx42Capability.PresetAdmission)||replacementIndex.Value is not int destination)throw new NotSupportedException("Full MX42 preset transfer requires a reviewed Scene-safe replacement transaction.");
            var document=PresetDocument.FromSnapshot(records.Where(r=>r.Type!=1).ToArray(),0x200);
            var canonical=BankService.Replacement(destination,document);
            if(records.Count!=canonical.Count||records.Zip(canonical).Any(p=>p.First.Type!=p.Second.Type||!p.First.Value.SequenceEqual(p.Second.Value)))throw new InvalidDataException("Malformed full-preset replacement envelope.");
            for(int slot=0;slot<11;slot++)
            {
                var effect=document.GetEffect(slot);var algorithm=catalog.Find(slot,effect.Code)??throw new NotSupportedException("Unknown replacement algorithm.");
                if(algorithm.Parameters.Any(p=>!float.IsFinite(effect.Parameters[p.Slot])))throw new InvalidDataException("Non-finite replacement DSP parameter.");
            }
            return;
        }
        foreach(var r in records)
        {
            var v=r.Value;int U(int at)=>BinaryPrimitives.ReadUInt16LittleEndian(v.AsSpan(at));
            if(r.Type==0&&v.SequenceEqual(new byte[4]))continue;
            if(r.Type==5&&v.Length==12&&v[0]==10&&v[1]==15&&(BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(8))&0xffff0000)==0x53430000&&v[8]<=10)continue;
            if(r.Type==0x1008&&v.Length==4&&U(0)==0&&U(2)<256)continue; // Explicit preset navigation.
            if(r.Type==0x1008&&v.Length==4&&DeviceSettings.Globals.Any(g=>g.Selector==U(0)&&BinaryPrimitives.ReadInt16LittleEndian(v.AsSpan(2))>=g.Minimum&&BinaryPrimitives.ReadInt16LittleEndian(v.AsSpan(2))<=g.Maximum))continue;
            if(r.Type==0x1008&&v.Length==4&&U(0)==24&&U(2)==3)continue; // Scenes template.
            if(r.Type==0x1008&&v.Length==4&&U(0)==39&&U(2)<3)continue;
            if(r.Type==0x1001&&v.Length==4&&(U(0)==0&&U(2)<=99||U(0)==1&&U(2)<=100||U(0)==2&&U(2)>=40&&U(2)<=250))continue;
            if(r.Type==0x1011&&v.Length==4&&U(0)==0&&U(2)<=1)continue;
            if(r.Type==0x100f&&v.Length==4&&U(0)<3&&v[2]<20&&v[3]<=26)continue;
            if(r.Type==0x1005&&v.Length==8&&DeviceSettings.Equalizer.Any(d=>d.Selector==U(0)&&BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(4))>=d.Minimum&&BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(4))<=d.Maximum))continue;
            if(Mx42ResourcePolicy.Accept(r))continue; // Existing native Stock handlers, native ARM/NOR replay verified.
            if(!activeIndex.Value.HasValue)throw new NotSupportedException("MX42 write requires an active-preset operation with a preflight and readback.");
            if(r.Type==0x2003&&v.Length==20&&U(0)==activeIndex.Value)continue;
            if(r.Type==0x16&&v.Length==8&&v[0]<11&&(v[0]!=5||Mx42Integration.Supports(Mx42Capability.UniversalSlotSix))&&v[1]==0&&U(2)==0&&catalog.Identity(v[0],BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4))).Known)continue;
            if(r.Type==0x16&&v.Length==8&&v[0]==5&&catalog.Identity(5,BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4))) is {Known:true,EffectiveFamily:5,IsNone:false})continue;
            if(r.Type==0x15&&v.Length==4&&v[0]<11&&v[1]<2&&U(2)==0)continue;
            if(r.Type==5&&v.Length==12&&v[0]<11&&v[1]<13&&(!(v[0]==10&&v[1]==12)||Mx42Integration.Supports(Mx42Capability.SlotElevenParameter12))&&U(2)==0)
            {
                var code=BinaryPrimitives.ReadUInt32LittleEndian(v.AsSpan(4));var id=catalog.Identity(v[0],code);float value=BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(8));
                if(float.IsFinite(value)&&(catalog.Find(v[0],code)?.Parameters.Any(p=>p.Slot==v[1])==true||id is {Known:true,IsNone:true}&&v[1]==0&&value>=0&&value<=11&&value==MathF.Truncate(value)))continue;
            }
            if(r.Type==6&&v.Length==4&&U(0)<=6&&(U(0)!=5||ChainRouting.ValidMode(U(2))))continue;
            if(r.Type is 7 or 9&&v.Length==20&&U(0)==activeIndex.Value)continue;
            if(r.Type==11&&v.Length==44&&U(0)==activeIndex.Value)continue;
            if(r.Type==8&&v.Length==16&&U(0)==activeIndex.Value&&v[2]<=v[3]&&v[3]<=11&&v.Skip(4).Take(11).Order().SequenceEqual(Enumerable.Range(0,11).Select(i=>(byte)i)))continue;
            if(r.Type==13&&v.Length==4&&(v[0]>>4)<3&&(v[0]&15)<3&&(v[1]<11||v[1]==255)&&v[2]<15)continue;
            if(r.Type==14&&v.Length==8&&v[0]<2&&(v[1]>>4)<3&&(v[1]&15)<3&&float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(4))))continue;
            if(r.Type==15&&v.Length==8&&v[0]<8)continue;
            if(r.Type==16&&v.Length==4&&v[0]<3&&(v[1]<=12||v[1]==255))continue;
            throw new NotSupportedException(Limitation);
        }
    }
    internal static void RequireScenes(){if(!CatalogRuntime.Current.IsMx42)throw new NotSupportedException("Scenes require MX42.");}
    internal static void RequireSceneBankAccess()
    {if(CatalogRuntime.Current.IsMx42&&!Mx42Integration.Supports(Mx42Capability.SceneSnapshot|Mx42Capability.SceneRestore|Mx42Capability.PresetAdmission))throw new NotSupportedException("MX42 status 5 cannot distinguish BASE with A–D from an empty bank. Lossless Scene backup/copy/import/move/swap needs a bank/context protocol. No recall or write sent.");}
}

