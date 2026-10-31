using System.Buffers.Binary;
using System.Text.Json;
namespace Gp200.Replica;
internal static class A2LiteModel
{
 internal const int WeightBytes=7484;
    internal static byte[] ReadWeights(string path)
    {
        if(!File.Exists(path)||new FileInfo(path).Length is <=0 or >8*1024*1024)throw new InvalidDataException("NAM must be between 1 byte and 8 MiB.");
        using var document=JsonDocument.Parse(File.ReadAllBytes(path));return ParseWeights(document.RootElement);
    }
    internal static byte[] ParseWeights(JsonElement document)
    {
        using var canonical=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","nam-a2-lite-config.json")));
        float[]? Try(JsonElement model)
        {
            if(!model.TryGetProperty("architecture",out var architecture)||architecture.GetString()!="WaveNet"||!model.TryGetProperty("sample_rate",out var sampleRate)||sampleRate.ValueKind!=JsonValueKind.Number||sampleRate.GetDouble()!=48000)return null;
            if(!model.TryGetProperty("config",out var config)||config.ValueKind!=JsonValueKind.Object||!config.TryGetProperty("head_scale",out var scale)||scale.ValueKind!=JsonValueKind.Number)return null;
            var props=config.EnumerateObject().ToArray();if(props.Length!=canonical.RootElement.EnumerateObject().Count()+1)return null;
            foreach(var expected in canonical.RootElement.EnumerateObject())if(!config.TryGetProperty(expected.Name,out var actual)||!Equal(actual,expected.Value))return null;
            if(props.Select(p=>p.Name).Distinct().Count()!=props.Length)return null;
            float hs=scale.GetSingle();if(!float.IsFinite(hs)||hs<=0||!model.TryGetProperty("weights",out var weights)||weights.ValueKind!=JsonValueKind.Array||weights.GetArrayLength()!=1871)return null;
            var array=new float[1871];int i=0;foreach(var w in weights.EnumerateArray()){if(w.ValueKind!=JsonValueKind.Number||!w.TryGetSingle(out float f)||!float.IsFinite(f))return null;array[i++]=f;}
            if(array[^1]<=0||Math.Abs(hs-array[^1])>1e-6f*Math.Max(Math.Abs(hs),Math.Abs(array[^1])))return null;return array;
        }
        var candidates=new List<float[]>();
        if(document.TryGetProperty("architecture",out var arch)&&arch.GetString()=="SlimmableContainer")
        {
            if(document.TryGetProperty("config",out var config)&&config.TryGetProperty("submodels",out var children)&&children.ValueKind==JsonValueKind.Array)
                foreach(var child in children.EnumerateArray())if(child.TryGetProperty("model",out var model)&&Try(model) is float[] candidate)candidates.Add(candidate);
        }
        else if(Try(document) is float[] single)candidates.Add(single);
        if(candidates.Count!=1)throw new InvalidDataException("Native NAM requires exactly one WaveNet A2 Lite at 48 kHz, 1871 finite weights and matching head_scale. Use Load SnapTone for other supported NAM models.");
        var w=candidates[0];var ordered=new List<float>(1871);ordered.AddRange(w[..3]);int at=3;
        for(int layer=0;layer<23;layer++)
        {
            int k=layer is 14 or 15?15:6;
            for(int t=0;t<k;t++)for(int i=0;i<3;i++)for(int o=0;o<3;o++)ordered.Add(w[at+(o*3+i)*k+t]);at+=9*k;
            ordered.AddRange(w[at..(at+6)]);for(int i=0;i<3;i++)for(int o=0;o<3;o++)ordered.Add(w[at+6+o*3+i]);ordered.AddRange(w[(at+15)..(at+18)]);at+=18;
        }
        for(int t=0;t<16;t++)for(int i=0;i<3;i++)ordered.Add(w[at+i*16+t]);ordered.AddRange(w[(at+48)..]);
        if(ordered.Count!=1871)throw new InvalidDataException("NAM layout mismatch.");byte[] result=new byte[WeightBytes];for(int i=0;i<ordered.Count;i++)BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i*4),ordered[i]);return result;
    }
    static bool Equal(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        return b.ValueKind switch
        {
            JsonValueKind.Object=>a.EnumerateObject().Count()==b.EnumerateObject().Count()&&b.EnumerateObject().All(p=>a.TryGetProperty(p.Name,out var v)&&Equal(v,p.Value)),
            JsonValueKind.Array=>a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(p=>Equal(p.First,p.Second)),
            JsonValueKind.Number=>a.GetDouble()==b.GetDouble(),JsonValueKind.String=>a.GetString()==b.GetString(),_=>a.GetRawText()==b.GetRawText()
        };
    }
}
