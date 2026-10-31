using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
namespace Gp200.Replica;

/// <summary>Native PRST plus exact Scene RAM and saved BASE. Device inventory is not embedded.</summary>
public sealed record Mx42PresetBundle(string Schema,byte[] BasePreset,byte[] LivePreset,byte[] Snapshot,string Digest)
{
    static string Hash(params byte[][] parts){using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);foreach(var p in parts)h.AppendData(p);return Convert.ToHexString(h.GetHashAndReset());}
    internal static Mx42PresetBundle Create(PresetDocument live,Mx42SceneSnapshot snapshot)
    {var b=snapshot.HasBase?snapshot.ApplySceneWords(live,SceneLayout.BaseWords):new PresetDocument(live.ToBytes());var x=b.ToBytes();var y=live.ToBytes();var z=snapshot.ToBytes();return new("gp200-mx42-scene-preset/v1",x,y,z,Hash(x,y,z));}
    public void Save(string path){Validate();File.WriteAllText(path,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
    public static Mx42PresetBundle Load(string path)
    {if(new FileInfo(path).Length>128*1024)throw new InvalidDataException("Scene backup exceeds 128 KiB.");var b=JsonSerializer.Deserialize<Mx42PresetBundle>(File.ReadAllText(path))??throw new InvalidDataException("Empty Scene backup.");b.Validate();return b;}
    internal Mx42SceneSnapshot Validate()
    {
        if(Schema!="gp200-mx42-scene-preset/v1"||BasePreset==null||LivePreset==null||Snapshot==null||Digest!=Hash(BasePreset,LivePreset,Snapshot))throw new InvalidDataException("Invalid MX42 Scene backup digest/schema.");
        if(!CatalogRuntime.Current.IsMx42)throw new NotSupportedException("An MX42 Scene bundle requires the MX42 profile.");
        var b=new PresetDocument(BasePreset);var live=new PresetDocument(LivePreset);_=BankService.Replacement(b.Index,b);_=BankService.Replacement(live.Index,live);var s=new Mx42SceneSnapshot(Snapshot);
        if(s.HasScenes&&!s.HasBase)throw new InvalidDataException("The saved BASE no longer matches the active topology. Save the preset first before copying its Scene bank.");
        var common=s.ApplySceneWords(b,SceneLayout.LiveWords);
        if(!PresetComparison.EquivalentFunctional(common,live,CatalogRuntime.Current))throw new InvalidDataException("BASE and live preset differ outside Scene-owned state.");
        var d=s.ToBytes();if(live.Index!=s.Slot||b.Index!=s.Slot||!live.Chain.SequenceEqual(d.AsSpan(SceneLayout.Order,SceneLayout.BlockCount).ToArray()))throw new InvalidDataException("Scene identity/order does not match the native preset.");
        for(int m=0;m<11;m++)
        {
            var a=CatalogRuntime.Current.Identity(m,live.GetEffect(m).Code);var z=CatalogRuntime.Current.Identity(m,b.GetEffect(m).Code);uint key=BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(SceneLayout.Keys+sizeof(uint)*m));
            if(!a.Known||!z.Known||a.EffectiveFamily!=z.EffectiveFamily||a.Ordinal!=z.Ordinal||key!=CatalogRuntime.Current.SceneKeyForIdentity(m,live.GetEffect(m).Code))throw new InvalidDataException("Scene effective family/algorithm does not match PRST.");
        }
        for(int m=0;m<11;m++)
        {
            int expected=m==10?SceneLayout.ChainMask:0;foreach(var parameter in CatalogRuntime.Current.Find(m,live.GetEffect(m).Code)!.Parameters)expected|=1<<parameter.Slot;
            if(BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(SceneLayout.Ownership+2*m))!=expected)throw new InvalidDataException("Scene mask does not match effective algorithm ownership.");
        }
        if(s.HasScenes)
        {
            foreach(int offset in SceneLayout.StoredStates)for(int m=0;m<11;m++)foreach(var parameter in CatalogRuntime.Current.Find(m,live.GetEffect(m).Code)!.Parameters)
            {float value=BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(offset+4*(m*15+parameter.Slot)));if(!float.IsFinite(value))throw new InvalidDataException("Scene owns a nonfinite DSP parameter.");}
            var scenes=Enumerable.Range(0,4).Select(a=>s.ApplySceneWords(live,SceneLayout.SceneAt(a))).ToArray();var topology=Mx42ChainConfig.Read(b);
            foreach(var scene in scenes.Append(b))
            {
                for(int m=0;m<11;m++)foreach(var parameter in CatalogRuntime.Current.Find(m,scene.GetEffect(m).Code)!.Parameters)if(!float.IsFinite(scene.GetEffect(m).Parameters[parameter.Slot]))throw new InvalidDataException("Scene owns a nonfinite DSP parameter.");
                var c=Mx42ChainConfig.Read(scene);if(c.Start!=topology.Start||c.Split!=topology.Split||c.End!=topology.End)throw new InvalidDataException("Scene CHAIN topology differs from its base.");
            }
            if(Sc10Capacity.Estimate(b,scenes)>SceneLayout.Sc10Capacity)throw new InvalidDataException("Scene bank exceeds the existing lossless SC10 capacity. No preset replacement sent.");
        }
        if(!SameWords(live,s.ApplySceneWords(live,SceneLayout.LiveWords))||s.HasBase&&!SameWords(b,s.ApplySceneWords(b,SceneLayout.BaseWords)))throw new InvalidDataException("Scene BASE/live words differ from their PRST.");return s;
    }
    internal static bool SameWords(PresetDocument a,PresetDocument b)=>Enumerable.Range(0,11).All(m=>a.GetEffect(m).Enabled==b.GetEffect(m).Enabled&&a.GetEffect(m).Parameters.Select(BitConverter.SingleToUInt32Bits).SequenceEqual(b.GetEffect(m).Parameters.Select(BitConverter.SingleToUInt32Bits)));
}

/// <summary>Uses the original native bank transport, STORE and LiveEditing. No Scene recapture loops.</summary>
public sealed class Mx42PresetTransactions
{
    readonly DeviceSession session;readonly BankService bank;readonly HashSet<int> modified=[];int ordinal;
    public string DirectoryPath {get;}
    public Mx42PresetTransactions(string operation):this(operation,DeviceSession.Current) { }
    internal Mx42PresetTransactions(string operation,DeviceSession session)
    {this.session=session;bank=new(session);using var operationContext=session.Enter();DirectoryPath=Path.Combine(AppPaths.Backups,$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-mx42-{operation}-{Guid.NewGuid():N}");Directory.CreateDirectory(DirectoryPath);}
    static PresetDocument At(PresetDocument p,int slot)=>PresetDocument.FromSnapshot(BankService.Replacement(slot,p).Where(r=>r.Type!=1).ToArray(),0x200);
    static bool Same(PresetDocument a,PresetDocument b)=>PresetComparison.EquivalentFunctional(At(a,b.Index),b,CatalogRuntime.Current);
    static void RequireWrite(){if(!CatalogRuntime.Current.IsMx42||!Mx42Integration.Supports(Mx42Capability.SceneSnapshot|Mx42Capability.SceneRestore|Mx42Capability.PresetAdmission))throw new NotSupportedException("Complete Scene restore and reviewed native preset replacement are not available on this firmware. No write sent.");}
    async Task Send(params WireRecord[] records)
    {
        DeviceResponse? r=null;string? error=null;
        try{r=await DeviceMidi.ExchangeAsync(0x12,records);if(r.CleanupErrors.Count!=0)throw new IOException("MIDI cleanup failed; inspect the device before continuing.");}
        catch(Exception ex){error=ex.Message;throw;}
        finally{EditorAuditLog.Supplement(Path.Combine(DirectoryPath,$"{++ordinal:0000}-native.json"),new{records,response=r,error});}
    }
    async Task Select(int slot)=>await bank.SelectCoreAsync(slot);
    async Task Reload(int slot){await Select(slot==0?1:0);await Select(slot);}
    async Task<Mx42PresetBundle> Capture(int slot)
    {
        await Select(slot);var before=await bank.ActiveAsync();var snapshot=await Mx42SceneUsb.Read();var after=await bank.ActiveAsync();
        if(before.Index!=snapshot.Slot||!Same(before,after)||!Mx42PresetBundle.SameWords(before,after))throw new IOException("Preset changed during Scene backup. No write sent.");
        CatalogRuntime.Current.ObserveCabSceneKeys(before,snapshot,after);
        var bundle=Mx42PresetBundle.Create(before,snapshot);bundle.Validate();return bundle;
    }
    async Task<Mx42PresetBundle> Backup(int slot)
    {var b=await Capture(slot);b.Save(Path.Combine(DirectoryPath,$"{++ordinal:0000}-{slot}-before.gpscene"));File.WriteAllBytes(Path.Combine(DirectoryPath,$"{ordinal:0000}-{slot}-stored-before.prst"),(await bank.ReadAsync(slot)).ToBytes());return b;}
    async Task Apply(PresetDocument p)
    {var live=await LiveEditing.ConnectCurrentAsync();await live.ApplyCore(At(p,live.Baseline.Index));var after=await bank.ActiveAsync();if(!Same(p,after))throw new IOException("Live native preset readback differs; no retry sent.");}
    async Task EnsureSceneState()
    {
        // Existing mode setter allocates its native State, without capture or recall.
        // Leaving Scene mode retains bank RAM; caller restores original global mode.
        var active=await bank.ActiveAsync();int mode=await SceneMailbox.Command(active.Index,active.GetEffect(10).Code,2,Path.Combine(DirectoryPath,$"{++ordinal:0000}-scene-init.json"),true,active);
        if(mode==4)throw new IOException("Native Scene runtime initialization was not confirmed.");
    }
    async Task Write(int slot,Mx42PresetBundle bundle)
    {
        RequireWrite();var source=bundle.Validate();if(!source.HasBase)source=source.WithBaseFromLive();var baseDoc=At(new PresetDocument(bundle.BasePreset),slot);var liveDoc=At(new PresetDocument(bundle.LivePreset),slot);
        await Mx42Integration.ValidatePresetPlan(baseDoc);
        await EnsureSceneState();
        // Invalidate destination Scene RAM through the existing native load path.
        await Select(slot==0?1:0);modified.Add(slot);
        using(var operation=Mx42Policy.ReplacementOperation(slot))await Send(BankService.Replacement(slot,baseDoc).ToArray());
        // Native saved-preset EXP/Quick export resolves against the CURRENT
        // models. Read back only after the destination is fully selected.
        await Select(slot);
        if(!Same(baseDoc,await bank.ReadAsync(slot)))throw new IOException("Native PRST replacement readback differs. Backup: "+DirectoryPath);
        var destination=await Mx42SceneUsb.Read();var baseBank=source.HasBase?source.AtBase():source.WithLive(baseDoc,255);
        await Mx42SceneUsb.Restore(baseBank,destination.Revision);
        using(var operation=Mx42Policy.ActiveOperation(slot))await Send(BankService.Commit(slot,baseDoc.Name));
        if(!Same(baseDoc,await bank.ReadAsync(slot)))throw new IOException("Native STORE readback differs. Backup: "+DirectoryPath);
        // Discard cache and verify actual SC10 decoded from NOR; never validate only RAM.
        await Reload(slot);var persisted=await Mx42SceneUsb.Read();
        if(!baseBank.SamePersistentBank(persisted))
        {File.WriteAllBytes(Path.Combine(DirectoryPath,"persisted-expected.bin"),baseBank.ToBytes());File.WriteAllBytes(Path.Combine(DirectoryPath,"persisted-actual.bin"),persisted.ToBytes());throw new IOException("Persisted SC10/BASE differs after reload. Backup: "+DirectoryPath);}
        if(!Mx42ChainUsb.HasSavedConfig(liveDoc))
        {Mx42ChainUsb.Project(liveDoc,await Mx42ChainUsb.Read());source=source.WithLive(liveDoc,source.Active);}
        await Apply(liveDoc);var current=await Mx42SceneUsb.Read();await Mx42SceneUsb.Restore(source,current.Revision);
    }
    async Task<T> Transaction<T>(Func<Task<T>> action,bool writing)
    {
        using var operationContext=session.Enter();
        if(writing)RequireWrite();await DeviceMidi.OperationGate.WaitAsync();
        try
        {
            modified.Clear();var original=await Capture((await bank.ActiveAsync()).Index);original.Save(Path.Combine(DirectoryPath,"active-working-before.gpscene"));int slot=new PresetDocument(original.LivePreset).Index;
            int originalMode=new DeviceSettings((await DeviceSession.Current.Read(ReadQuery.State)).Records).Global(24);
            // On failure, preserve evidence and stop; no uncertain rollback/replay.
            T result=await action();
            if(!modified.Contains(slot))
            {await Select(slot);await EnsureSceneState();await Apply(new PresetDocument(original.LivePreset));var current=await Mx42SceneUsb.Read();await Mx42SceneUsb.Restore(original.Validate(),current.Revision);}
            else await Select(slot);
            await Send(new WireRecord(0x1008,[(byte)24,0,(byte)originalMode,0]));
            if(new DeviceSettings((await DeviceSession.Current.Read(ReadQuery.State)).Records).Global(24)!=originalMode)throw new IOException("Original footswitch mode was not restored.");
            return result;
        }
        finally{DeviceMidi.OperationGate.Release();}
    }
    public async Task<Mx42PresetBundle> ExportAsync(int slot,bool working=false)
    {
        using var operationContext=session.Enter();
        if(working)
        {
            await DeviceMidi.OperationGate.WaitAsync();
            try{if((await bank.ActiveAsync()).Index==slot)return await Backup(slot);}
            finally{DeviceMidi.OperationGate.Release();}
        }
        if(!Mx42Integration.Supports(Mx42Capability.SceneSnapshot|Mx42Capability.SceneRestore))throw new NotSupportedException("Exporting another stored preset requires complete original-context restoration.");
        return await Transaction(async()=>{if(!working)await Reload(slot);return await Backup(slot);},false);
    }
    public Task ImportAsync(int slot,Mx42PresetBundle bundle)=>Transaction(async()=>{bundle.Validate();await Backup(slot);await Write(slot,bundle);return true;},true);
    public Task RenameAsync(int slot,string name)=>Transaction(async()=>
    {var b=await Backup(slot);var live=BankService.RenamedCopy(new PresetDocument(b.LivePreset),name);_=BankService.Commit(slot,live.Name);await Write(slot,Mx42PresetBundle.Create(live,b.Validate()));return true;},true);
    public Task SaveAsync(int destination)=>Transaction(async()=>{var b=await Capture((await bank.ActiveAsync()).Index);await Backup(destination);await Write(destination,b);return true;},true);
    public Task CopyAsync(int source,int destination,bool move=false)=>Transaction(async()=>
    {if(source==destination)return true;var a=await Backup(source);await Backup(destination);await Write(destination,a);if(move)await WriteEmpty(source);return true;},true);
    public Task SwapAsync(int first,int second)=>Transaction(async()=>
    {if(first==second)return true;var a=await Backup(first);var b=await Backup(second);await Write(first,b);await Write(second,a);return true;},true);
    async Task WriteEmpty(int slot)
    {var p=new PresetDocument(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","clear-default-gp200-1.8.2.prst")));await WritePlain(slot,p);}
    async Task WritePlain(int slot,PresetDocument p)
    {
        RequireWrite();var target=At(p,slot);
        for(int m=0;m<11;m++)
        {var effect=target.GetEffect(m);if(!CatalogRuntime.Current.Identity(m,effect.Code).Known)throw new InvalidDataException("Unknown MX42 effect identity; no replacement sent.");foreach(var parameter in CatalogRuntime.Current.Find(m,effect.Code)?.Parameters??[])if(!float.IsFinite(effect.Parameters[parameter.Slot]))throw new InvalidDataException("Nonfinite DSP parameter; no replacement sent.");}
        await Mx42Integration.ValidatePresetPlan(target);await EnsureSceneState();await Select(slot==0?1:0);modified.Add(slot);
        using(var operation=Mx42Policy.ReplacementOperation(slot))await Send(BankService.Replacement(slot,target).ToArray());await Select(slot);
        var current=await Mx42SceneUsb.Read();var empty=current.WithoutScenes();
        // Native STORE upgrades legacy Stock CHAIN metadata to MX42 words13/14.
        // Predict only that documented normalization; keep mode0/1 and S/R.
        if(!Mx42ChainUsb.HasSavedConfig(target))Mx42ChainUsb.Project(target,await Mx42ChainUsb.Read());await Mx42SceneUsb.Restore(empty,current.Revision);
        using(var operation=Mx42Policy.ActiveOperation(slot))await Send(BankService.Commit(slot,target.Name));await Reload(slot);
        var stored=await bank.ReadAsync(slot);var finalBank=await Mx42SceneUsb.Read();
        if(!Same(target,stored)||finalBank.HasScenes)
        {File.WriteAllBytes(Path.Combine(DirectoryPath,"plain-expected.prst"),target.ToBytes());File.WriteAllBytes(Path.Combine(DirectoryPath,"plain-actual.prst"),stored.ToBytes());File.WriteAllBytes(Path.Combine(DirectoryPath,"plain-bank.bin"),finalBank.ToBytes());throw new IOException("Empty Scene bank/preset did not persist. Backup: "+DirectoryPath);}
    }
    public Task ReplaceManyAsync(int first,IReadOnlyList<PresetDocument> docs,IProgress<int>? progress=null)=>Transaction(async()=>
    {if(first<0||first+docs.Count>256)throw new ArgumentOutOfRangeException(nameof(first));foreach(var d in docs)_=BankService.Replacement(first,d);for(int i=0;i<docs.Count;i++)await Backup(first+i);for(int i=0;i<docs.Count;i++){await WritePlain(first+i,docs[i]);progress?.Report(i+1);}return true;},true);
    public Task CopyBankAsync(int source,int destination,bool move=false)=>Transaction(async()=>
    {CheckBank(source,destination);if(source==destination)return true;var a=new List<Mx42PresetBundle>();for(int i=0;i<4;i++)a.Add(await Backup(source+i));for(int i=0;i<4;i++)await Backup(destination+i);for(int i=0;i<4;i++)await Write(destination+i,a[i]);if(move)for(int i=0;i<4;i++)await WriteEmpty(source+i);return true;},true);
    public Task SwapBankAsync(int first,int second)=>Transaction(async()=>
    {CheckBank(first,second);if(first==second)return true;var a=new List<Mx42PresetBundle>();var b=new List<Mx42PresetBundle>();for(int i=0;i<4;i++)a.Add(await Backup(first+i));for(int i=0;i<4;i++)b.Add(await Backup(second+i));for(int i=0;i<4;i++){await Write(first+i,b[i]);await Write(second+i,a[i]);}return true;},true);
    static void CheckBank(int a,int b){if(a%4!=0||b%4!=0||a<0||b<0||a>252||b>252)throw new ArgumentOutOfRangeException();}
}
