using System.Buffers.Binary;
using System.Text.Json;

namespace Gp200.Replica;

public sealed record GlobalSettingDefinition(int Selector,int Offset,string Name,int Minimum,int Maximum,string[]? Choices=null)
{
    public int Read(byte[] value)=>Selector is 12 or 40?value[Offset]:BinaryPrimitives.ReadInt16LittleEndian(value.AsSpan(Offset));
}
public sealed record EqSettingDefinition(int Selector,string Name,float Minimum,float Maximum,float? Default);
public sealed record DeviceSettingChange(WireRecord Record,ushort SnapshotType,int Offset,byte[] Expected,string Description)
{
    public bool Matches(IReadOnlyList<WireRecord> records)
    {
        var candidates=records.Where(r=>r.Type==SnapshotType);
        if(Record.Type==0x100f){int template=BinaryPrimitives.ReadUInt16LittleEndian(Record.Value);candidates=candidates.Skip(template).Take(1);}
        return candidates.Any(r=>r.Value.Length>=Offset+Expected.Length&&r.Value.AsSpan(Offset,Expected.Length).SequenceEqual(Expected));
    }
}
public sealed class DeviceSettings
{
    public IReadOnlyList<WireRecord> Records {get;}
    public DeviceSettings(IReadOnlyList<WireRecord> records)
    {
        Records=records.Select(r=>new WireRecord(r.Type,(byte[])r.Value.Clone())).ToArray();
        if(Get(0x1007).Length!=72||Get(0x1004).Length!=124)throw new InvalidDataException(UiText.T("Estado global/EQ incompleto."));
    }
    public byte[] Get(ushort type)=>Records.First(r=>r.Type==type).Value;
    public int Global(int selector)=>Globals.Single(g=>g.Selector==selector).Read(Get(0x1007));
    public float Eq(int selector)=>BinaryPrimitives.ReadSingleLittleEndian(Get(0x1004).AsSpan(selector*4));
    public int Footswitch(int template,int slot)=>Records.Where(r=>r.Type==0x100e).ElementAt(template).Value[20+slot];
    public int Drum(int selector)=>BinaryPrimitives.ReadUInt16LittleEndian(Get(0x1002).AsSpan(selector switch{0=>0,1=>4,2=>2,_=>throw new ArgumentOutOfRangeException(nameof(selector))}));
    public bool DrumPlaying=>BinaryPrimitives.ReadUInt16LittleEndian(Get(0x1000))!=0;
    public static readonly string[] Actions=["None","CTRL 1","CTRL 2","CTRL 3","CTRL 4","Looper","Drum","Tuner","TAP","Bank +","Bank −","Patch +","Patch −","Patch A","Patch B","Patch C","Patch D","Bank","Drum Patch +","Drum Patch −","EXP1 A/B","MIDI","Bypass","CTRL 5","CTRL 6","CTRL 7","CTRL 8"];
    public static readonly GlobalSettingDefinition[] Globals=[
        new(1,6,"Input Level (dB)",-20,20),new(2,8,"Input Mode",0,2,["A.GT","E.GT","Line"]),
        new(3,10,"No CAB L",0,1,["OFF","ON"]),new(4,12,"No CAB R",0,1,["OFF","ON"]),
        new(5,14,"USB Rec Level (dB)",-20,20),new(6,16,"USB Rec Mode L",0,1,["Dry","Wet"]),new(7,18,"USB Rec Mode R",0,1,["Dry","Wet"]),
        new(8,20,"AUX to USB",0,1,["OFF","ON"]),new(9,22,"USB Monitor (dB)",-20,20),
        new(12,45,"EXP2 / Footswitch",0,2,["Pedal","Single Footswitch","Double Footswitch"]),
        new(14,28,"MIDI In Source",0,2,["DIN Only","USB Only","Mixed"]),
        new(15,30,"Input CH DIN",0,16,["Omni",..Enumerable.Range(1,16).Select(i=>i.ToString())]),new(16,32,"Input CH USB",0,16,["Omni",..Enumerable.Range(1,16).Select(i=>i.ToString())]),
        new(17,34,"Output CH DIN",-1,16,["OFF","Omni",..Enumerable.Range(1,16).Select(i=>i.ToString())]),new(18,36,"Output CH USB",-1,16,["OFF","Omni",..Enumerable.Range(1,16).Select(i=>i.ToString())]),
        new(19,38,"Clock Source",0,4,["Internal","DIN Only","USB Only","External","Mixed"]),
        new(20,40,"Clock Out DIN",0,1,["OFF","ON"]),new(21,42,"Clock Out USB",0,1,["OFF","ON"]),
        new(24,48,"FS Template",0,2,["Preset","Stomp","User"]),new(30,58,"Global EQ",0,1,["OFF","ON"]),
        new(31,60,"EXT FS1 Tap",0,26,Actions),new(33,64,"EXT FS2 Tap",0,26,Actions),new(35,68,"FS Mode",0,1,["Wait","Initial"]),new(36,70,"Auto CAB Match",0,1,["OFF","ON"]),
        new(40,57,"Device Display",0,2,["Footswitch","Patch","Signal Chain"])
    ];
    public static readonly string[] DrumPatterns=["Classic Rock 1","Classic Rock 2","Classic Rock 3","Classic Rock 4","Classic Rock 5","Classic Rock 6","Hard Rock 1","Hard Rock 2","Hard Rock 3","Post Rock 1","Post Rock 2","Post Rock 3","Garage Rock","Prog Rock","Surf Rock","Punk 1","Punk 2","Punk 3","Punk 4","Post Punk 1","Post Punk 2","Heavy Metal 1","Heavy Metal 2","Nu-Metal 1","Nu-Metal 2","Hardcore","EMO","Grunge","New Wave","Rock5/4","Funk 1","Funk 2","Funk 3","Funk 4","Jazz Funk 1","Jazz Funk 2","Jazz Funk 3","Blues 1","Blues 2","Blues 3","Blues 4","Swing","Shuffle","Shuffle 3/4","Bluegrass","Country","Country Folk","Pop 1","Pop 2","Pop 3","Hip Hop 1","Hip Hop 2","Hip Hop 3","Hip Hop Rock","Pub","Jazz 1","Jazz 2","Jazz 3","Jazz 4","Bossanova 1","Bossanova 2","Fusion","Electro1","Electro2","Techno","TripHop","Electronic Pop","Break Beat","Drum&Bass","Latin 1","Latin 2","Latin 3","Latin Pop 1","Latin Pop 2","Samba","Tango","Beguine","Ska","Polka","Waltz","Reggae 1","Reggae 2","Mazuke","Musette","March 1","March 2","March 3","New Age 1","New Age 2","World","1/4","2/4","3/4","4/4","5/4","6/4","7/4","6/8","7/8","9/8"];
    public static readonly EqSettingDefinition[] Equalizer=BuildEq();
    static EqSettingDefinition[] BuildEq()
    {
        var rows=new List<EqSettingDefinition>{new(1,UiText.T("Low Cut (19 = extremo mínimo)"),19,20000,19)};
        int[] selectors=[8,12,16,20];float[] defaults=[100,500,1000,5000];
        for(int i=0;i<4;i++){rows.Add(new(selectors[i],UiText.T($"Banda {i+1} · Hz"),20,20000,defaults[i]));rows.Add(new(selectors[i]+2,UiText.T($"Banda {i+1} · dB"),-20,20,0));rows.Add(new(selectors[i]+1,UiText.T($"Banda {i+1} · Q"),.1f,10,.7f));}
        rows.Add(new(28,UiText.T("High Cut (20001 = extremo máximo)"),20,20001,20001));rows.Add(new(30,"Master Volume",0,100,null));return rows.ToArray();
    }
    public static DeviceSettingChange SetGlobal(int selector,int value)
    {
        var definition=Globals.SingleOrDefault(g=>g.Selector==selector)??throw new ArgumentOutOfRangeException(nameof(selector));
        if(value<definition.Minimum||value>definition.Maximum)throw new ArgumentOutOfRangeException(nameof(value));
        byte[] payload=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(payload,(ushort)selector);BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2),(short)value);
        byte[] expected=selector is 12 or 40?[(byte)value]:payload[2..4];
        return new(new(0x1008,payload),0x1007,definition.Offset,expected,$"{definition.Name} = {value}");
    }
    public static DeviceSettingChange SetEq(int selector,float value)
    {
        var definition=Equalizer.SingleOrDefault(g=>g.Selector==selector)??throw new ArgumentOutOfRangeException(nameof(selector));
        if(!float.IsFinite(value)||value<definition.Minimum||value>definition.Maximum)throw new ArgumentOutOfRangeException(nameof(value));
        byte[] payload=new byte[8];BinaryPrimitives.WriteUInt16LittleEndian(payload,(ushort)selector);BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4),value);
        return new(new(0x1005,payload),0x1004,selector*4,payload[4..8],$"EQ {definition.Name} = {value}");
    }
    public static DeviceSettingChange SetFootswitch(int template,int slot,int action)
    {
        if(template is <0 or >2||slot is <0 or >19||action is <0 or >26)throw new ArgumentOutOfRangeException(nameof(slot));
        byte[] payload=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(payload,(ushort)template);payload[2]=(byte)slot;payload[3]=(byte)action;
        return new(new(0x100f,payload),0x100e,20+slot,[(byte)action],$"FS {template+1}/{slot+1} = {Actions[action]}");
    }
    public static WireRecord ResetFootswitchCommand(int template)
    {
        if(template is <0 or >2)throw new ArgumentOutOfRangeException(nameof(template));
        return new(0x1008,[39,0,(byte)template,0]);
    }
    public static async Task<DeviceSettings> ResetFootswitchAsync(int template)
    {
        var command=ResetFootswitchCommand(template);
        await DeviceMidi.OperationGate.WaitAsync();try
        {
            var before=await DeviceSession.Current.Read(ReadQuery.State);Check(before);
            var previous=new DeviceSettings(before.Records);
            string folder=Path.Combine(AppPaths.Backups);Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,$"footswitch-reset-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            File.WriteAllText(path,JsonSerializer.Serialize(new{template,before},new JsonSerializerOptions{WriteIndented=true}));
            Check(await DeviceMidi.SendSettingsAsync(command));await Task.Delay(100);
            var after=await DeviceSession.Current.Read(ReadQuery.State);Check(after);
            var result=new DeviceSettings(after.Records);
            for(int other=0;other<3;other++)for(int slot=0;slot<20;slot++)
                if(other!=template&&result.Footswitch(other,slot)!=previous.Footswitch(other,slot))
                    throw new IOException(UiText.T("Otra plantilla cambió durante el reset. Se conserva la copia previa en ")+path);
            EditorAuditLog.Supplement(path+".after.json",after);
            return result;
        }finally{DeviceMidi.OperationGate.Release();}
    }
    public static DeviceSettingChange SetDrum(int selector,int value)
    {
        bool valid=selector switch{0=>value is >=0 and <=99,1=>value is >=0 and <=100,2=>value is >=40 and <=250,_=>false};
        if(!valid)throw new ArgumentOutOfRangeException(nameof(value));
        byte[] payload=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(payload,(ushort)selector);BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2),(ushort)value);
        return new(new(0x1001,payload),0x1002,selector switch{0=>0,1=>4,_=>2},payload[2..4],$"Drum {selector} = {value}");
    }
    public static DeviceSettingChange SetDrumPlaying(bool playing)=>new(new(0x1011,[0,0,playing?(byte)1:(byte)0,0]),0x1000,0,[playing?(byte)1:(byte)0,0],playing?"Drum Play":"Drum Stop");
    static void Check(DeviceResponse response){if(response.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver no liberó los puertos MIDI; detén las operaciones y vuelve a conectar."));}
    public static async Task<DeviceSettings> ReadAsync()
    {
        await DeviceMidi.OperationGate.WaitAsync();try{var response=await DeviceSession.Current.Read(ReadQuery.State);Check(response);return new(response.Records);}finally{DeviceMidi.OperationGate.Release();}
    }
    public static async Task<DeviceSettings> ApplyAsync(DeviceSettingChange change)
    {
        await DeviceMidi.OperationGate.WaitAsync();try{
        var before=await DeviceSession.Current.Read(ReadQuery.State);
        Check(before);
        if(change.Matches(before.Records))return new(before.Records);
        var sent=await DeviceMidi.SendSettingsAsync(change.Record);
        Check(sent);
        var after=await DeviceSession.Current.Read(ReadQuery.State);
        Check(after);
        string directory=AppPaths.Evidence;
        string path=Path.Combine(directory,$"settings-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
        EditorAuditLog.Supplement(path,new{change.Description,change.Record,before,sent,after,verified=change.Matches(after.Records)});
        if(!change.Matches(after.Records))throw new IOException(UiText.T("El estado leído no confirma el ajuste. Se ha detenido la operación; recarga antes de reintentar."));
        return new(after.Records);
        }finally{DeviceMidi.OperationGate.Release();}
    }
}


