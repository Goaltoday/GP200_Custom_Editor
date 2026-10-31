using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Gp200.Replica;

public enum EditorProfile { Stock, MX42 }
public sealed record EffectIdentity(int PhysicalSlot,int EffectiveFamily,int Ordinal,uint WireCode,bool IsNone,bool Known);

public sealed partial class Catalog
{
    public EditorProfile Profile {get;private set;}=EditorProfile.Stock;
    public bool IsMx42=>Profile==EditorProfile.MX42;
    readonly Dictionary<(int family,int ordinal),AlgorithmInfo> mxAlgorithms=[];
    int[] mxCounts=[];
    readonly Dictionary<uint,AlgorithmInfo> mxCabTemplates=[];
    uint[] mxCabBaseline=[];
    readonly Dictionary<uint,uint> mxObservedCabSceneKeys=[];
    internal bool HasMx42DeviceCabMap {get;private set;}
    public string[] Mx42NamLabels {get;private set;}=Enumerable.Range(1,32).Select(i=>$"NAM {i:00} · installed name unknown").ToArray();
    internal const string MxBaseHash="533e11db06afd16950a57df710e85537626551fca84040c7db09d3c58e8514e8";
    public static readonly string[] FamilyNames=[..PresetDocument.Modules,"NAM"];
    internal void ApplyMx42()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","mx42-catalog.json")));
        var root=doc.RootElement;
        if(root.GetProperty("schema").GetString()!="gp200-mx42-catalog/v1"||root.GetProperty("sourceBinSha256").GetString()!=MxBaseHash)throw new InvalidDataException("Unknown MX42 catalog provenance.");
        mxCounts=root.GetProperty("counts").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        if(mxCounts.Length!=12||mxCounts.Any(c=>c<1||c>256)||mxCounts[11]!=32)throw new InvalidDataException("Invalid universal catalog counts.");
        foreach(var a in root.GetProperty("algorithms").EnumerateArray())
        {
            int f=a.GetProperty("family").GetInt32(),i=a.GetProperty("ordinal").GetInt32();
            if(f is <0 or >11||i<0||i>=mxCounts[f])throw new InvalidDataException("Invalid universal algorithm identity.");
            var parameters=a.GetProperty("parameters").EnumerateArray().Select(p=>new ParameterInfo(p.GetProperty("index").GetInt32(),p.GetProperty("name").GetString()!,p.GetProperty("kind").GetString()!,p.GetProperty("unit").GetString()!,p.GetProperty("min").ToString()+" … "+p.GetProperty("max"),p.GetProperty("default").GetSingle().ToString("R",CultureInfo.InvariantCulture)){
                Attributes=p.GetProperty("attributes").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()!),
                Menus=p.GetProperty("menus").EnumerateArray().Select(m=>new ParameterMenu(m.GetProperty("Name").GetString()!,m.GetProperty("Value").GetSingle())).ToArray()
            }).ToArray();
            if(parameters.Any(p=>p.Slot is <0 or >12)||parameters.Select(p=>p.Slot).Distinct().Count()!=parameters.Length)throw new InvalidDataException("MX42 DSP/control word ownership collision.");
            mxAlgorithms.Add((f,i),new(FamilyNames[f],a.GetProperty("nativeCode").GetUInt32(),a.GetProperty("name").GetString()!,parameters,null){ResourceName=a.GetProperty("resourceName").GetString()!});
        }
        if(mxAlgorithms.Count!=mxCounts.Sum())throw new InvalidDataException("Incomplete universal catalog.");
        mxCabBaseline=Enumerable.Range(0,mxCounts[5]).Select(i=>mxAlgorithms[(5,i)].Code).ToArray();
        foreach(uint id in mxCabBaseline)mxCabTemplates.Add(id,mxAlgorithms[(5,Array.IndexOf(mxCabBaseline,id))]);
        Profile=EditorProfile.MX42;
        FirmwareHash="MX42-catalog-"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","mx42-catalog.json"))));
        ProfileStatus="MX42 · selected locally, not discovered by USB · native live editing · protocol limits apply · NAM installed names unknown";
    }
    public EffectIdentity Identity(int slot,uint code)
    {
        if(slot is <0 or >10)throw new ArgumentOutOfRangeException(nameof(slot));
        if(!IsMx42)return new(slot,slot,-1,code,false,Find(slot,code)!=null);
        int prefix=(int)(code>>24);
        if(prefix==0xea){int i=(int)(code&65535);bool ok=(code&0xffff0000)==0xea0b0000&&i<=32;return new(slot,11,i,code,ok&&i==32,ok);}
        if(prefix is >=0xe5 and <=0xe9)
        {
            int f=(int)((code>>16)&15),i=(int)(code&65535);bool ok=(code&0x00f00000)==0&&f<12&&i<=mxCounts[f];
            return new(slot,f,i,code,ok&&i==mxCounts[f],ok);
        }
        foreach(var pair in mxAlgorithms.Where(x=>x.Key.family==slot))if(pair.Value.Code==code)return new(slot,slot,pair.Key.ordinal,code,false,true);
        return new(slot,-1,-1,code,false,false);
    }
    internal AlgorithmInfo? MxFind(int slot,uint code)
    {
        var id=Identity(slot,code);if(!id.Known)return null;
        var algorithm=id.IsNone?new AlgorithmInfo(FamilyNames[id.EffectiveFamily],code,"NONE",[],null){ResourceName="NONE"}:mxAlgorithms[(id.EffectiveFamily,id.Ordinal)];
        if(id.EffectiveFamily==11&&!id.IsNone)algorithm=algorithm with{Name=Mx42NamLabels[id.Ordinal]};
        return algorithm with{Code=code,PhysicalSlot=slot,FamilyId=id.EffectiveFamily,Ordinal=id.Ordinal,IsNone=id.IsNone};
    }
    public AlgorithmInfo[] ForFamily(int slot,int family)
    {
        if(!IsMx42)return ForModule(slot);
        if(slot is <0 or >10||family is <0 or >11)throw new ArgumentOutOfRangeException();
        return Enumerable.Range(0,mxCounts[family]+1).Select(i=>MxFind(slot,WireCode(slot,family,i))!).ToArray();
    }
    public uint WireCode(int slot,int family,int ordinal)
    {
        if(!IsMx42||slot is <0 or >10||family is <0 or >11||ordinal<0||ordinal>mxCounts[family])throw new ArgumentOutOfRangeException();
        if(family==11)return 0xea0b0000u|(uint)ordinal;
        return family==slot&&ordinal<mxCounts[family]?mxAlgorithms[(family,ordinal)].Code:0xe9000000u|((uint)family<<16)|(uint)ordinal;
    }
    public AlgorithmInfo[] ForEffect(int slot,uint code)=>IsMx42&&Identity(slot,code) is {Known:true} id?ForFamily(slot,id.EffectiveFamily):ForModule(slot);
    public string FamilyLabel(int slot,uint code)=>IsMx42?(Identity(slot,code) is {Known:true} id?FamilyNames[id.EffectiveFamily]:"UNKNOWN"):PresetDocument.Modules[slot];
    public static bool ControlWord(Catalog catalog,int module,int slot)=>catalog.IsMx42&&module==10&&(catalog.IsMx42?slot is 13 or 14:slot is >=12 and <=14);
    internal void InvalidateMx42DeviceNames()
    {
        if(!IsMx42)return;
        Mx42NamLabels=Enumerable.Range(1,32).Select(i=>$"NAM {i:00} · installed name unknown").ToArray();
    }
    internal void AcceptMx42DeviceNames(string[] labels)
    {
        if(!IsMx42||labels.Length!=32)throw new InvalidDataException("Invalid NAM device labels");
        Mx42NamLabels=labels;
    }
    internal void UpdateMxName(int family,uint nativeCode,string name)
    {
        foreach(var key in mxAlgorithms.Where(p=>p.Key.family==family&&p.Value.Code==nativeCode).Select(p=>p.Key).ToArray())mxAlgorithms[key]=mxAlgorithms[key] with{Name=name};
        if(family==5&&mxCabTemplates.TryGetValue(nativeCode,out var template))mxCabTemplates[nativeCode]=template with{Name=name};
    }
    // CAB code low bits are stable native IDs, not ordinals. Only a complete
    // device table can reorder templates and the universal CAB/NONE namespace.
    internal void ApplyMx42CabMap(IReadOnlyList<uint> ids)
    {
        if(!IsMx42||ids.Count is <1 or >256||ids.Distinct().Count()!=ids.Count||ids.Any(id=>!mxCabTemplates.ContainsKey(id)))throw new InvalidDataException("Invalid or unsupported complete MX42 CAB identity map.");
        var rows=ids.Select(id=>mxCabTemplates[id]).ToArray();
        foreach(var key in mxAlgorithms.Keys.Where(k=>k.family==5).ToArray())mxAlgorithms.Remove(key);
        for(int i=0;i<rows.Length;i++)mxAlgorithms.Add((5,i),rows[i]);
        mxCounts[5]=rows.Length;mxObservedCabSceneKeys.Clear();HasMx42DeviceCabMap=true;
    }
    internal void ClearDeviceCabMap()
    {
        if(!IsMx42)return;
        foreach(var key in mxAlgorithms.Keys.Where(k=>k.family==5).ToArray())mxAlgorithms.Remove(key);
        for(int i=0;i<mxCabBaseline.Length;i++)mxAlgorithms.Add((5,i),mxCabTemplates[mxCabBaseline[i]]);
        mxCounts[5]=mxCabBaseline.Length;mxObservedCabSceneKeys.Clear();HasMx42DeviceCabMap=false;
    }
    internal uint SceneKeyForIdentity(int slot,uint code)
    {
        var id=Identity(slot,code);if(!id.Known)throw new InvalidDataException("Unknown MX42 identity in Scene context.");
        if(!HasMx42DeviceCabMap&&slot==5&&id.EffectiveFamily==5&&!id.IsNone&&(code>>24)==0x0a)
        {
            if(mxObservedCabSceneKeys.TryGetValue(code,out uint observed))return observed;
            if(Mx42Integration.Supports(Mx42Capability.SceneSnapshot))throw new NotSupportedException("This firmware does not publish the complete CAB map. Read a stable live Scene context to verify this CAB identity before exporting or copying; no write was sent.");
        }
        return (uint)id.EffectiveFamily<<24|(uint)id.Ordinal;
    }
    // Called only by the stable USB capture path, never by bundle/file import.
    // One observed legacy native CAB pair does not reorder the catalog or grant
    // any unobserved code an ordinal. New connections discard all such pairs.
    internal void ObserveCabSceneKeys(PresetDocument before,Mx42SceneSnapshot snapshot,PresetDocument after)
    {
        if(!IsMx42||HasMx42DeviceCabMap)return;
        var data=snapshot.ToBytes();
        if(before.Index!=snapshot.Slot||before.Index!=after.Index||!PresetComparison.EquivalentFunctional(before,after,this)||!Mx42PresetBundle.SameWords(before,after)||!before.Chain.SequenceEqual(data.AsSpan(77,11).ToArray())||data[88]!=before.RoutingRaw[0]||data[89]!=before.RoutingRaw[1]||data[92]!=before.FxLoopMode||!Mx42PresetBundle.SameWords(before,snapshot.ApplySceneWords(before,3480)))throw new InvalidDataException("CAB identity observation requires an unchanged live preset and Scene context.");
        var candidate=new Dictionary<uint,uint>(mxObservedCabSceneKeys);
        for(int slot=0;slot<11;slot++)
        {
            uint code=before.GetEffect(slot).Code;if(after.GetEffect(slot).Code!=code)throw new InvalidDataException("CAB observation changed algorithm identity.");
            var id=Identity(slot,code);uint key=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(94+4*slot));
            if(!id.Known||data[66+slot]!=(key&65535))throw new InvalidDataException("Invalid Scene selection identity for CAB observation.");
            int expectedMask=slot==10?0x6000:0;foreach(var parameter in Find(slot,code)!.Parameters)expectedMask|=1<<parameter.Slot;
            if(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(138+2*slot))!=expectedMask)throw new InvalidDataException("CAB observation ownership mask differs.");
            if(slot==5&&id.EffectiveFamily==5&&!id.IsNone&&(code>>24)==0x0a)
            {
                if((key>>24)!=5||(key&0x00ffffff)>=mxCounts[5]||candidate.TryGetValue(code,out uint old)&&old!=key||candidate.Any(pair=>pair.Key!=code&&pair.Value==key))throw new InvalidDataException("Conflicting or invalid native CAB Scene identity.");
                candidate[code]=key;
            }
            else if(key!=((uint)id.EffectiveFamily<<24|(uint)id.Ordinal))throw new InvalidDataException("Non-CAB Scene identity differs during CAB observation.");
        }
        mxObservedCabSceneKeys.Clear();foreach(var pair in candidate)mxObservedCabSceneKeys.Add(pair.Key,pair.Value);
    }
}

public sealed record Mx42ChainConfig(int Start,int Split,int End,int Align,bool Inverted,float Blend)
{
    public static Mx42ChainConfig Decode(uint word,float blend)
    {
        var c=new Mx42ChainConfig((int)(word>>8)&15,(int)(word>>12)&15,(int)(word>>16)&15,(int)(word&127)-32,(word&128)!=0,blend);
        if((word&0xfff00000)!=0x43400000||!c.Valid)throw new InvalidDataException("Invalid MX42 CHAIN packed word/BLEND.");return c;
    }
    public bool Valid=>Start>=0&&Start<=Split&&Split<=End&&End<=11&&Align is >=-32 and <=32&&float.IsFinite(Blend)&&Blend is >=0 and <=100;
    public uint Pack()=>Valid?0x43400000u|((uint)End<<16)|((uint)Split<<12)|((uint)Start<<8)|(Inverted?128u:0)|(uint)(Align+32):throw new InvalidDataException("Invalid MX42 CHAIN config.");
    public static Mx42ChainConfig Read(PresetDocument p)
    {var e=p.GetEffect(10);return Decode(BitConverter.SingleToUInt32Bits(e.Parameters[13]),e.Parameters[14]);}
    public void Write(PresetDocument p)
    {
        var e=p.GetEffect(10);var v=e.Parameters.ToArray();v[13]=BitConverter.UInt32BitsToSingle(Pack());v[14]=Blend;p.SetEffect(10,e.Enabled,v);
    }
    public static Mx42ChainConfig GetOrDefault(PresetDocument p)
    {try{return Read(p);}catch(InvalidDataException){int s=p.RoutingRaw[0],r=p.RoutingRaw[1],split=ChainRouting.Extended(p.FxLoopMode)?p.FxLoopMode&15:s;return new(s,split,r,0,false,50);}}
}



