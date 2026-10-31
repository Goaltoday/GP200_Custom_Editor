namespace Gp200.Replica;

public static class CatalogRuntime
{
    public static Catalog Current => DeviceSession.Current.Catalog;
}
public sealed partial class Catalog
{
    public string ProfileStatus { get; private set; } = "Stock";
    public string? FirmwareHash
    {
        get; private set;
    }
    public static Catalog Configured(AppPreferences p)
    {
        var c = new Catalog(Path.Combine(AppContext.BaseDirectory, "Data", "algorithms.json"));
        if (p.DeviceProfile == "MX42")
            c.ApplyMx42();
        return c;
    }
    public void RequireParameters(int module, uint code)
    {
        if (Find(module, code)?.ParametersKnown != true)
            throw new InvalidOperationException("Parameter metadata is unavailable for this effect.");
    }
}
