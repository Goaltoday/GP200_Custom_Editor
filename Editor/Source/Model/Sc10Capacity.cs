namespace Gp200.Replica;
internal static class Sc10Capacity
{
    // Same greedy lossless codec as persist.inc: reference runs precede small
    // integer literals; raw float words cost five bytes. No firmware encoder runs.
    internal static int Estimate(PresetDocument p,IReadOnlyList<PresetDocument> scenes)
    {
        if(scenes.Count==0)return 0;if(scenes.Count!=4)throw new InvalidDataException("SC10 requires four Scenes.");var catalog=CatalogRuntime.Current;var indices=new List<(int module,int slot)>();
        for(int m=0;m<11;m++)
        {
            catalog.RequireParameters(m,p.GetEffect(m).Code);var a=catalog.Find(m,p.GetEffect(m).Code)!;
            foreach(int s in a.Parameters.Select(x=>x.Slot).Concat(m==10?new[]{13,14}:Array.Empty<int>()).Distinct().Order())indices.Add((m,s));
        }
        int Word(PresetDocument d,int i){var x=indices[i];return BitConverter.SingleToInt32Bits(d.GetEffect(x.module).Parameters[x.slot]);}
        int size=74;
        for(int a=0;a<4;a++)for(int i=0;i<indices.Count;)
        {
            int run=0;while(i+run<indices.Count&&run<255&&Word(scenes[a],i+run)==Word(a==0?p:scenes[0],i+run))run++;
            if(run>0){size+=2;i+=run;continue;}
            int word=Word(scenes[a],i);bool small=Enumerable.Range(0,201).Any(n=>BitConverter.SingleToInt32Bits((float)n)==word);size+=small?1:5;i++;
        }
        return size;
    }
}
