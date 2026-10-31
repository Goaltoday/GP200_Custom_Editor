using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Gp200.Replica;

public sealed record BankEntry(int Slot,PresetDocument Document)
{
    public string Position=>$"{Slot/4+1:00}-{(char)('A'+Slot%4)}";
    public string Name=>Document.Name;
    public override string ToString()=>$"{Position}   {Name}";
}

/// <summary>GP-200 preset library operations. Backups precede every persistent replacement.
/// Firmware messages are absent. Successful writes require saved-slot readback.</summary>
public sealed class BankService
{
    readonly DeviceSession session;
    public BankService():this(DeviceSession.Current) { }
    internal BankService(DeviceSession session) { this.session=session; }
    ushort format {get=>session.Format;set=>session.Format=value;}
    SemaphoreSlim operations=>DeviceMidi.OperationGate;
    public string? LastBackupDirectory {get;private set;}
    public static string Position(int slot){CheckSlot(slot);return $"{slot/4+1:00}-{(char)('A'+slot%4)}";}
    internal static int AdjacentSlot(int slot,int delta){CheckSlot(slot);if(delta is not (-1 or 1))throw new ArgumentOutOfRangeException(nameof(delta));return Math.Clamp(slot+delta,0,255);}
    static void CheckSlot(int slot){if(slot is <0 or >255)throw new ArgumentOutOfRangeException(nameof(slot));}
    static void Check(DeviceResponse r){if(r.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver no liberó los puertos MIDI: ")+string.Join(", ",r.CleanupErrors));}
    async Task Send(IReadOnlyList<WireRecord> records)
    {
        if(CatalogRuntime.Current.IsMx42&&records.Any(r=>r.Type is 2 or 0x2009))
            throw new InvalidOperationException("MX42 preset replacement requires a Scene-safe native transaction. No write was sent.");
        string directory=LastBackupDirectory??Path.Combine(AppPaths.Evidence);Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,$"bank-exchange-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        DeviceResponse? response=null;string? error=null;
        try{response=await DeviceMidi.ExchangeAsync(0x12,records);Check(response);}
        catch(Exception ex){error=ex.Message;throw;}
        finally{EditorAuditLog.Supplement(path,new{utc=DateTime.UtcNow,command=0x12,requested=records,response,error});}
    }
    async Task EnsureFormat()
    {
        if(format!=0)return;
        var state=await DeviceSession.Current.Read(ReadQuery.State);Check(state);
        format=BinaryPrimitives.ReadUInt16LittleEndian(state.Records.Single(r=>r.Type==0x1007&&r.Value.Length==72).Value);
    }
    public async Task<PresetDocument> ReadAsync(int slot)
    {
        using var operationContext=session.Enter();
        CheckSlot(slot);await EnsureFormat();var response=await DeviceSession.Current.Read(ReadQuery.SavedPreset,slot);Check(response);
        var doc=PresetDocument.FromSnapshot(response.Records,format);
        if(doc.Index!=slot)throw new InvalidDataException(UiText.T("La GP-200 respondió con otra posición."));
        return doc;
    }
    public async Task<PresetDocument> ActiveAsync()
    {
        using var operationContext=session.Enter();
        await EnsureFormat();var response=await DeviceSession.Current.Read(ReadQuery.ActivePreset);Check(response);var p=PresetDocument.FromSnapshot(response.Records,format);if(Mx42ChainUsb.Available)Mx42ChainUsb.Normalize(p,await Mx42ChainUsb.Read());return p;
    }
    public async Task<IReadOnlyList<BankEntry>> ReadLibraryAsync(IProgress<int>? progress=null,CancellationToken cancel=default)
    {
        using var operationContext=session.Enter();
        var entries=new List<BankEntry>(256);
        for(int slot=0;slot<256;slot++){cancel.ThrowIfCancellationRequested();entries.Add(new(slot,await ReadAsync(slot)));progress?.Report(slot+1);}
        return entries;
    }
    internal static WireRecord Selection(int slot)
    {
        CheckSlot(slot);byte[] value=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(2),(ushort)slot);return new(0x1008,value);
    }
    internal static WireRecord Commit(int slot,string name)
    {
        CheckSlot(slot);if(name.Length>16||name.Any(c=>c<32||c>126))throw new ArgumentException(UiText.T("El nombre debe tener hasta 16 caracteres ASCII imprimibles."));
        byte[] value=new byte[20];BinaryPrimitives.WriteUInt16LittleEndian(value,(ushort)slot);Encoding.ASCII.GetBytes(name).CopyTo(value,4);return new(0x2003,value);
    }
    internal static WireRecord Clear(int slot)
    {
        CheckSlot(slot);byte[] value=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(value,(ushort)slot);return new(0x2009,value);
    }
    internal Dictionary<string,double> LastSelectionTiming {get;private set;}=new();
    public async Task<PresetDocument> SelectAsync(int slot,bool reload=false)
    {
        using var operationContext=session.Enter();
        await operations.WaitAsync();
        try{return await SelectCoreAsync(slot,reload,preserveUnsaved:true);}
        finally{operations.Release();}
    }
    // Caller owns OperationGate. Both UI navigation and Scene transactions use
    // this one selection/send/readiness/readback primitive; backup is caller policy.
    internal async Task<PresetDocument> SelectCoreAsync(int slot,bool reload=false,bool preserveUnsaved=false)
    {
        using var operationContext=session.Enter();CheckSlot(slot);
        var timer=System.Diagnostics.Stopwatch.StartNew();LastSelectionTiming=new();double last=0;
        void Mark(string stage){double now=timer.Elapsed.TotalMilliseconds;LastSelectionTiming[stage]=now-last;last=now;}
        var before=await ActiveAsync();Mark("readBefore");
        if(before.Index==slot&&!reload)
        {
            if(session.Catalog.IsMx42&&Mx42Integration.Supports(Mx42Capability.SceneSnapshot))
            {await Mx42Integration.WaitForIdle();return await ConfirmSelection(slot);}
            return before;
        }
        if(preserveUnsaved)
        {
            var stored=await ReadAsync(before.Index);Mark("readSaved");LastBackupDirectory=null;
            if(!Equivalent(before,stored))BackupSelection(before,stored);Mark("backup");
        }
        await Send([Selection(slot)]);Mark("send");
        if(session.Catalog.IsMx42&&Mx42Integration.Supports(Mx42Capability.SceneSnapshot))await Mx42Integration.WaitForIdle();
        else await Task.Delay(80);
        Mark("settle");var actual=await ConfirmSelection(slot);Mark("readAfter");return actual;
    }
    async Task<PresetDocument> ConfirmSelection(int slot)
    {
        var actual=await ActiveAsync();
        if(actual.Index!=slot)throw new IOException("The GP-200 did not confirm the selected position. No selection was repeated.");
        return actual;
    }
    void BackupSelection(PresetDocument active,PresetDocument stored)
    {
        // Both snapshots were read under OperationGate immediately before selection.
        // Archive those exact bytes; repeating the reads adds latency and can archive a different state.
        string directory=Path.Combine(AppPaths.Backups,$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-select-unsaved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);LastBackupDirectory=directory;
        File.WriteAllBytes(Path.Combine(directory,$"{Position(stored.Index)}.prst"),stored.ToBytes());
        File.WriteAllBytes(Path.Combine(directory,"active-unsaved.prst"),active.ToBytes());
        File.WriteAllText(Path.Combine(directory,"operation.json"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow,operation="select-unsaved",slots=new[]{active.Index},status="backup-complete",source="preflight-snapshots"},new JsonSerializerOptions{WriteIndented=true}));
    }
    async Task<PresetDocument> BackupAsync(string operation,IEnumerable<int> slots)
    {
        string directory=Path.Combine(AppPaths.Backups,$"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{operation}-{Guid.NewGuid():N}");Directory.CreateDirectory(directory);LastBackupDirectory=directory;
        foreach(int slot in slots.Distinct()){var doc=await ReadAsync(slot);File.WriteAllBytes(Path.Combine(directory,$"{Position(slot)}.prst"),doc.ToBytes());}
        var active=await ActiveAsync();File.WriteAllBytes(Path.Combine(directory,"active-unsaved.prst"),active.ToBytes());
        File.WriteAllText(Path.Combine(directory,"operation.json"),JsonSerializer.Serialize(new{utc=DateTime.UtcNow,operation,slots=slots.ToArray(),status="backup-complete"},new JsonSerializerOptions{WriteIndented=true}));return active;
    }
    internal static bool Equivalent(PresetDocument a,PresetDocument b)
    {
        return CatalogRuntime.Current.IsMx42?PresetComparison.EquivalentFunctional(a,b,CatalogRuntime.Current):PresetComparison.Equivalent(a,b);
    }
    async Task<PresetDocument> WaitSaved(int slot,PresetDocument expected)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();PresetDocument actual;
        do{actual=await ReadAsync(slot);if(Equivalent(expected,actual))return actual;await Task.Delay(100);}while(timer.ElapsedMilliseconds<3000);
        throw new IOException(UiText.T("La lectura de ")+Position(slot)+UiText.T(" no coincide con la escritura. No se ha repetido el envío. Copia previa: ")+LastBackupDirectory);
    }
    public async Task<PresetDocument> SaveActiveAsync(int slot)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {
            CheckSlot(slot);await operations.WaitAsync();
            try{var active=await ActiveAsync();if(active.Index==slot)return await SaveInPlaceCore(active);}
            finally{operations.Release();}
            var scenes=new Mx42PresetTransactions("save-as");LastBackupDirectory=scenes.DirectoryPath;await scenes.SaveAsync(slot);return await ReadAsync(slot);
        }
        CheckSlot(slot);await operations.WaitAsync();try
        {
            var active=await ActiveAsync();
            _=Commit(slot,active.Name);
            var expected=PresetDocument.FromSnapshot(Replacement(slot,active).Where(r=>r.Type!=1).ToArray(),format);
            await BackupAsync(active.Index==slot?"save":"save-as",[active.Index,slot]);await Send([Commit(slot,active.Name)]);await Task.Delay(120);
            return await WaitSaved(slot,expected);
        }finally{operations.Release();}
    }
    // Native STORE keeps the current Scene RAM and commits it through the
    // firmware's own persistence hook. Rebuilding/selecting the destination is
    // necessary for replacement/save-as, never for saving the active position.
    internal async Task<PresetDocument> SaveInPlaceCore(PresetDocument active)
    {
        Mx42NamLimit.Validate(active);
        var current=await BackupAsync("save-in-place",[active.Index]);
        File.WriteAllText(Path.Combine(LastBackupDirectory!,"scope.txt"),"Native STORE of active preset and firmware-managed Scenes. PRST backup does not contain the previous persisted SC10; export .gpscene separately for a complete historical Scene backup.");
        // BackupAsync just read and archived this exact active snapshot under OperationGate.
        // Reuse it for preflight instead of requesting the same full snapshot twice.
        if(current.Index!=active.Index||!PresetComparison.EquivalentFunctional(active,current,CatalogRuntime.Current))throw new IOException("Active preset changed before STORE; no write sent.");
        using var activeOperation=Mx42Policy.ActiveOperation(active.Index);
        await Send([Commit(active.Index,active.Name)]);
        var saved=await WaitSaved(active.Index,active);
        if(!PresetComparison.EquivalentFunctional(active,await ActiveAsync(),CatalogRuntime.Current))throw new IOException("Active state changed during STORE; inspect the device.");
        return saved;
    }
    public async Task<PresetDocument> ClearAsync(int slot)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {
            var empty=new PresetDocument(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","clear-default-gp200-1.8.2.prst")));
            return await ReplaceAsync(slot,empty);
        }
        CheckSlot(slot);await operations.WaitAsync();try
        {
            await BackupAsync("clear",[slot]);await Send([Clear(slot)]);await Task.Delay(120);
            var actual=await ReadAsync(slot);
            EditorAuditLog.SupplementBytes(Path.Combine(LastBackupDirectory!,"clear-actual.prst"),actual.ToBytes());
            if(!IsCleared(actual))
                throw new IOException(UiText.T("No se ha confirmado el reinicio del preset. Consulta la copia previa en ")+LastBackupDirectory+UiText.T("."));
            return actual;
        }finally{operations.Release();}
    }
    internal static IReadOnlyList<WireRecord> Replacement(int slot,PresetDocument document)
    {
        Mx42NamLimit.Validate(document);
        CheckSlot(slot);var records=document.SnapshotRecords().Select(r=>new WireRecord(r.Type,(byte[])r.Value.Clone())).ToList();
        if(records.Any(r=>r.Type is not (2 or 8 or 12 or 15 or 16 or 20)))throw new InvalidDataException(UiText.T("El preset contiene registros que no se han confirmado para transferencia completa."));
        // Preserve every unknown byte and float bit pattern. Only slot-bearing fields change.
        foreach(var record in records.Where(r=>r.Type is 2 or 8))BinaryPrimitives.WriteUInt16LittleEndian(record.Value,(ushort)slot);
        if(records.Count(r=>r.Type==2)!=1||records.Count(r=>r.Type==8)!=1||records.Count(r=>r.Type==20)!=11||records.Count(r=>r.Type==12)!=9||records.Count(r=>r.Type==16)!=3)
            throw new InvalidDataException(UiText.T("El archivo no contiene un preset completo."));
        // Four-CTRL legacy files are expanded using the official blank assignment shape.
        var controllers=records.Where(r=>r.Type==15).ToArray();
        if(controllers.Length is not (4 or 8)||!controllers.Select(r=>r.Value[0]).Order().SequenceEqual(Enumerable.Range(0,controllers.Length).Select(i=>(byte)i)))throw new InvalidDataException(UiText.T("Asignaciones CTRL incompletas o ambiguas."));
        for(int i=controllers.Length;i<8;i++){byte[] value=new byte[8];value[0]=(byte)i;records.Add(new(15,value));}
        byte[] context=new byte[4];context[0]=1;BinaryPrimitives.WriteUInt16LittleEndian(context.AsSpan(2),(ushort)slot);
        byte[] range=new byte[4];BinaryPrimitives.WriteUInt16LittleEndian(range,(ushort)slot);BinaryPrimitives.WriteUInt16LittleEndian(range.AsSpan(2),(ushort)slot);
        var result=new[]{new WireRecord(0,context),new WireRecord(1,range)}.Concat(records).ToArray();
        // Validate complete record lengths, module uniqueness and required sections before sending.
        _=PresetDocument.FromSnapshot(result.Where(r=>r.Type!=1).ToArray(),0x200);
        return result;
    }
    internal static bool IsCleared(PresetDocument actual)
    {
        // Captured hardware reset on the user's stock 1.8.2: not the editor's
        // local EMPTY/80 template. Preserve the complete capture as evidence.
        var template=new PresetDocument(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data","clear-default-gp200-1.8.2.prst")));
        var expected=PresetDocument.FromSnapshot(Replacement(actual.Index,template).Where(r=>r.Type!=1).ToArray(),0x200);
        return PresetComparison.EquivalentFunctional(expected,actual,CatalogRuntime.Current);
    }
    async Task<PresetDocument> ReplaceCore(int slot,PresetDocument document)
    {
        var message=Replacement(slot,document);await Send(message);await Task.Delay(150);
        var expected=PresetDocument.FromSnapshot(message.Where(r=>r.Type!=1).ToArray(),format);
        return await WaitSaved(slot,expected);
    }
    internal static PresetDocument RenamedCopy(PresetDocument source,string name)
    {
        var result=new PresetDocument(source.ToBytes());result.SetGeneral(name.Trim(),result.Volume,result.Tempo);return result;
    }
    public async Task<PresetDocument> RenameAsync(int slot,string name)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42){CheckSlot(slot);Mx42Policy.RequireSceneBankAccess();var transaction=new Mx42PresetTransactions("rename");LastBackupDirectory=transaction.DirectoryPath;await transaction.RenameAsync(slot,name);return await ReadAsync(slot);}
        CheckSlot(slot);await operations.WaitAsync();try
        {
            var before=await ReadAsync(slot);var changed=RenamedCopy(before,name);
            if(changed.Name==before.Name)return before;
            await BackupAsync("rename",[slot]);
            // Native manager 0x597cd0: type 7 updates the indexed name only.
            byte[] value=new byte[20];BinaryPrimitives.WriteUInt16LittleEndian(value,(ushort)slot);
            Encoding.ASCII.GetBytes(changed.Name).CopyTo(value,4);
            await Send([new WireRecord(7,value)]);await Task.Delay(120);
            return await WaitSaved(slot,changed);
        }finally{operations.Release();}
    }
    public async Task<PresetDocument> ReplaceAsync(int slot,PresetDocument document)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {CheckSlot(slot);var scenes=new Mx42PresetTransactions("replace");LastBackupDirectory=scenes.DirectoryPath;await scenes.ReplaceManyAsync(slot,[document]);return await ReadAsync(slot);}
        _=Replacement(slot,document);await operations.WaitAsync();try{await BackupAsync("replace",[slot]);return await ReplaceCore(slot,document);}finally{operations.Release();}
    }
    public async Task ReplaceManyAsync(int first,IReadOnlyList<PresetDocument> documents,IProgress<int>? progress=null)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {var scenes=new Mx42PresetTransactions("replace-many");LastBackupDirectory=scenes.DirectoryPath;await scenes.ReplaceManyAsync(first,documents,progress);return;}
        CheckSlot(first);if(documents.Count==0||first+documents.Count>256)throw new ArgumentOutOfRangeException(nameof(documents));
        for(int i=0;i<documents.Count;i++)_=Replacement(first+i,documents[i]);
        await operations.WaitAsync();try
        {
            await BackupAsync("replace-many",Enumerable.Range(first,documents.Count));
            for(int i=0;i<documents.Count;i++){await ReplaceCore(first+i,documents[i]);progress?.Report(i+1);}
        }finally{operations.Release();}
    }
    public async Task CopyAsync(int source,int destination,bool move=false)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {CheckSlot(source);CheckSlot(destination);var scenes=new Mx42PresetTransactions(move?"move":"copy");LastBackupDirectory=scenes.DirectoryPath;await scenes.CopyAsync(source,destination,move);return;}
        CheckSlot(source);CheckSlot(destination);if(source==destination)return;await operations.WaitAsync();try
        {
            // Official manager operation3 copies the active working model when its
            // index matches the source, including edits not yet stored to flash.
            var active=await ActiveAsync();var original=active.Index==source?active:await ReadAsync(source);_=Replacement(destination,original);await BackupAsync(move?"move":"copy",[source,destination]);await ReplaceCore(destination,original);
            if(move){await Send([Clear(source)]);await Task.Delay(120);var empty=await ReadAsync(source);if(!IsCleared(empty))throw new IOException(UiText.T("Destino copiado, pero no se confirmó el vaciado del origen. Consulta ")+LastBackupDirectory);}
        }finally{operations.Release();}
    }
    public async Task SwapAsync(int first,int second)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {CheckSlot(first);CheckSlot(second);var scenes=new Mx42PresetTransactions("swap");LastBackupDirectory=scenes.DirectoryPath;await scenes.SwapAsync(first,second);return;}
        CheckSlot(first);CheckSlot(second);if(first==second)return;await operations.WaitAsync();try
        {
            var active=await ActiveAsync();var a=active.Index==first?active:await ReadAsync(first);var b=active.Index==second?active:await ReadAsync(second);_=Replacement(first,b);_=Replacement(second,a);await BackupAsync("swap",[first,second]);
            await ReplaceCore(first,b);await ReplaceCore(second,a);
        }finally{operations.Release();}
    }
    public async Task CopyBankAsync(int source,int destination)
    {
        using var operationContext=session.Enter();
        if(CatalogRuntime.Current.IsMx42)
        {var scenes=new Mx42PresetTransactions("copy-bank");LastBackupDirectory=scenes.DirectoryPath;await scenes.CopyBankAsync(source,destination);return;}
        var docs=new List<PresetDocument>();for(int i=0;i<4;i++)docs.Add(await ReadAsync(source+i));await ReplaceManyAsync(destination,docs);
    }
    static void RequireMx42BankOperation(int first,int second)
    {
        if(!CatalogRuntime.Current.IsMx42)throw new NotSupportedException("Complete bank move/swap is available only in the MX42 profile.");
        Mx42Policy.RequireSceneBankAccess();CheckSlot(first);CheckSlot(second);
        if(first%4!=0||second%4!=0)throw new ArgumentException("Bank operations require the first position of each four-preset bank.");
    }
    public async Task MoveBankAsync(int source,int destination)
    {
        using var operationContext=session.Enter();
        RequireMx42BankOperation(source,destination);if(source==destination)return;
        var transaction=new Mx42PresetTransactions("move-bank");LastBackupDirectory=transaction.DirectoryPath;
        await transaction.CopyBankAsync(source,destination,true);
    }
    public async Task SwapBankAsync(int first,int second)
    {
        using var operationContext=session.Enter();
        RequireMx42BankOperation(first,second);if(first==second)return;
        var transaction=new Mx42PresetTransactions("swap-bank");LastBackupDirectory=transaction.DirectoryPath;
        await transaction.SwapBankAsync(first,second);
    }
    public async Task<PresetDocument> ImportSceneAsync(int slot,ScenePresetBundle bundle)
    {
        using var operationContext=session.Enter();CheckSlot(slot);var scenes=new Mx42PresetTransactions("import");LastBackupDirectory=scenes.DirectoryPath;bundle.Validate();await scenes.ImportAsync(slot,bundle.Mx42!);return await ReadAsync(slot);}
}
