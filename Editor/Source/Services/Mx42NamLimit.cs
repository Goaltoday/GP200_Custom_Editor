namespace Gp200.Replica;
internal static class Mx42NamLimit
{
    internal static void Validate(PresetDocument document)
    {
        var catalog=CatalogRuntime.Current;
        if(!catalog.IsMx42)return;
        int count=0;
        for(int slot=0;slot<11;slot++)
        {
            var identity=catalog.Identity(slot,document.GetEffect(slot).Code);
            if(identity.EffectiveFamily==11&&!identity.IsNone)count++;
        }
        if(count>1)throw new InvalidOperationException("MX42 permits one selected NAM block per preset, including bypassed blocks. Set additional NAM blocks to NONE. The 32 stored models remain available.");
    }
}
