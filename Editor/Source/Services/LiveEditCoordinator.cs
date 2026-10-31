namespace Gp200.Replica;

internal sealed class LiveEditCoordinator
{
    internal PresetDocument? Pending
    {
        get; set;
    }
    internal PresetDocument? Desired
    {
        get; set;
    }
    internal bool Running
    {
        get; private set;
    }
    internal void Queue(PresetDocument candidate)
    {
        Pending = Desired = candidate;
    }
    internal PresetDocument? Take()
    {
        var p = Pending;
        Pending = null;
        return p;
    }
    internal void Clear()
    {
        Pending = Desired = null;
    }
    internal void Validate(LiveEditing live,PresetDocument candidate)
    {
        using var context=live.Session.Enter();
        _=LiveEditing.Build(live.Baseline,candidate,live.Session.Catalog);
    }
    internal async Task RunAsync(LiveEditing live,LibraryEntry entry,Action? sending=null,Action? confirmed=null)
    {
        if(Running)throw new InvalidOperationException("An edit is already running.");
        using var context=live.Session.Enter();Running=true;
        try
        {
            while(Pending!=null)
            {
                await Task.Delay(15);var candidate=Take()!;Validate(live,candidate);
                sending?.Invoke();var previous=entry.Document.ToBytes();
                var actual=await live.ApplyAsync(candidate);
                Confirm(entry,actual,previous);confirmed?.Invoke();
            }
            Desired=null;
        }
        catch{Clear();throw;}
        finally{Running=false;}
    }
    internal async Task CommitAsync(LiveEditing live,LibraryEntry entry,PresetDocument candidate,bool? redo)
    {
        if(Running)throw new InvalidOperationException("An edit is already running.");
        using var context=live.Session.Enter();Validate(live,candidate);Running=true;
        try{var previous=entry.Document.ToBytes();var actual=await live.ApplyAsync(candidate);Confirm(entry,actual,previous,redo);}
        finally{Running=false;}
    }
    internal static void Confirm(LibraryEntry entry, PresetDocument actual, byte[] previous, bool? redo = null)
    {
        if (redo.HasValue)
        {
            var from = redo.Value ? entry.Redo : entry.Undo;
            var to = redo.Value ? entry.Undo : entry.Redo;
            from.Pop();
            to.Push(previous);
        }
        else
        {
            entry.Undo.Push(previous);
            entry.Redo.Clear();
        }
        entry.Document = actual;
    }
}
