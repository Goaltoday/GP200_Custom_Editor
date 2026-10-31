namespace Gp200.Replica;
public static class PresetComparison
{
    public static bool EquivalentStoredSettings(PresetDocument active,PresetDocument stored,Catalog catalog)
    {
        // EXP-controlled positions are runtime values. The assignments and their
        // endpoints still participate in the comparison; direct UI edits are
        // tracked separately by the editor's undo history.
        var adjusted=new PresetDocument(active.ToBytes());
        foreach(var group in active.Expression.Concat(stored.Expression).Where(a=>a.Module<11&&a.Parameter<15).GroupBy(a=>a.Module))
        {
            var current=adjusted.GetEffect(group.Key);var saved=stored.GetEffect(group.Key);if(current.Code!=saved.Code)continue;
            var values=(float[])current.Parameters.Clone();foreach(var a in group)if(float.IsFinite(saved.Parameters[a.Parameter]))values[a.Parameter]=saved.Parameters[a.Parameter];
            adjusted.SetEffect(group.Key,current.Enabled,values);
        }
        return EquivalentFunctional(adjusted,stored,catalog);
    }
    public static bool EquivalentFunctional(PresetDocument a,PresetDocument b,Catalog catalog)
    {
        var left=a.SnapshotRecords();var right=b.SnapshotRecords();if(left.Count!=right.Count)return false;
        foreach(var (x,y) in left.Zip(right))
        {
            // Special Quick destinations have no effect parameter. Stock bank
            // captures use 0; the official UI emits the unused marker 255.
            if(x.Type==16&&y.Type==16&&x.Value.Length==4&&y.Value.Length==4&&x.Value[0]==y.Value[0]&&x.Value[1]==y.Value[1]&&x.Value[1] is 11 or 12)
            {
                ushort xp=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(x.Value.AsSpan(2)),yp=System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(y.Value.AsSpan(2));
                if(xp is 0 or 255&&yp is 0 or 255)continue;
            }
            // Firmware zeroes inactive EXP endpoints when destination is None.
            // Stored files may retain previous endpoints; they are not active.
            if(x.Type==12&&y.Type==12&&x.Value.Length==12&&y.Value.Length==12&&x.Value[1]==255&&y.Value[1]==255)
            {
                if(!x.Value.AsSpan(0,4).SequenceEqual(y.Value.AsSpan(0,4)))return false;
                continue;
            }
            // Byte after the one-byte FX mode, before name[16], changes to
            // stack-like values after a name edit. Preserve it in files; it
            // has no known setter and is excluded only from functional sync.
            if(x.Type==2&&y.Type==2&&x.Value.Length==88&&y.Value.Length==88)
            {
                if(!x.Value.AsSpan(0,6).SequenceEqual(y.Value.AsSpan(0,6))||!x.Value.AsSpan(7,8).SequenceEqual(y.Value.AsSpan(7,8))||!x.Value.AsSpan(16).SequenceEqual(y.Value.AsSpan(16)))return false;
                continue;
            }
            if(x.Type==20&&y.Type==20&&x.Value.Length==68&&y.Value.Length==68&&x.Value.AsSpan(0,8).SequenceEqual(y.Value.AsSpan(0,8)))
            {
                var code=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(x.Value.AsSpan(4));var algorithm=catalog.Find(x.Value[0],code);
                if(algorithm!=null&&algorithm.ParametersKnown)
                {
                    foreach(var parameter in algorithm.Parameters)if(!x.Value.AsSpan(8+parameter.Slot*4,4).SequenceEqual(y.Value.AsSpan(8+parameter.Slot*4,4)))return false;
                    if(catalog.IsMx42&&x.Value[0]==10){int first=catalog.IsMx42?13:12;if(!x.Value.AsSpan(8+first*4,(15-first)*4).SequenceEqual(y.Value.AsSpan(8+first*4,(15-first)*4)))return false;}
                    continue;
                }
            }
            if(!RecordEquivalent(x,y))return false;
        }
        return true;
    }
    // Observed varying on consecutive read-only snapshots; native producers leave
    // these padding bytes uninitialized. Keep them in PRST, ignore only for sync.
    public static bool Equivalent(PresetDocument a,PresetDocument b)
    {
        var left=a.SnapshotRecords();var right=b.SnapshotRecords();
        if(left.Count!=right.Count)return false;
        foreach(var (x,y) in left.Zip(right))
        {
            if(!RecordEquivalent(x,y))return false;
        }
        return true;
    }
    public static bool RecordEquivalent(WireRecord x,WireRecord y)
    {
        if(x.Type!=y.Type||x.Value.Length!=y.Value.Length)return false;
        for(int i=0;i<x.Value.Length;i++)
        {
            if(x.Type==8&&i==15||x.Type==15&&i is 2 or 3)continue;
            if(x.Value[i]!=y.Value[i])return false;
        }
        return true;
    }
}
