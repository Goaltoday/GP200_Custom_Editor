using System.Security.Cryptography;
using System.Text.Json;
namespace Gp200.Replica;

/// <summary>Compatible MX42 file envelope. Native firmware owns SC10 persistence.</summary>
public sealed record ScenePresetBundle(string Schema,string FirmwareHash,byte[] Preset,byte[][] Scenes,string Digest)
{
    public Mx42PresetBundle? Mx42 {get;init;}
    internal static ScenePresetBundle Wrap(Mx42PresetBundle bundle)
    {
        bundle.Validate();byte[] preset=(byte[])bundle.LivePreset.Clone();
        return new("gp200-mx42-scene-envelope/v1",CatalogRuntime.Current.FirmwareHash??"MX42",preset,[],Hash(preset,[])){Mx42=bundle};
    }
    static string Hash(byte[] p,byte[][] scenes)
    {using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);h.AppendData(p);foreach(var s in scenes)h.AppendData(s);return Convert.ToHexString(h.GetHashAndReset());}
    public void Save(string path){Validate();File.WriteAllText(path,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
    public static ScenePresetBundle Load(string path)
    {
        if(new FileInfo(path).Length>128*1024)throw new InvalidDataException("Scene file exceeds 128 KiB.");
        string text=File.ReadAllText(path);using var json=JsonDocument.Parse(text);
        if(!json.RootElement.TryGetProperty("Schema",out var schema)||schema.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Scene file schema is missing or invalid.");
        if(schema.GetString()=="gp200-mx42-scene-preset/v1")
            return Wrap(Mx42PresetBundle.Load(path));
        if(schema.GetString()!="gp200-mx42-scene-envelope/v1")throw new NotSupportedException("Custom5 Scene files are not supported. No write was sent.");
        var b=JsonSerializer.Deserialize<ScenePresetBundle>(text)??throw new InvalidDataException("Empty Scene file.");b.Validate();return b;
    }
    internal void Validate()
    {
        Mx42Policy.RequireScenes();
        if(Mx42!=null||Schema=="gp200-mx42-scene-envelope/v1")
        {
            if(!CatalogRuntime.Current.IsMx42||Schema!="gp200-mx42-scene-envelope/v1"||Mx42==null||Preset==null||Scenes==null||Scenes.Length!=0||Digest!=Hash(Preset,Scenes))throw new InvalidDataException("Invalid MX42 Scene envelope or different profile.");
            Mx42.Validate();if(!Preset.SequenceEqual(Mx42.LivePreset))throw new InvalidDataException("MX42 envelope preset differs from its complete Scene bundle.");return;
        }
        throw new NotSupportedException("Custom5 Scene files are not supported. No write was sent.");
    }
}
