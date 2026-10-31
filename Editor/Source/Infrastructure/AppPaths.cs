namespace Gp200.Replica;

internal static class AppPaths
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GP200 Replica");
    internal static string Backups => Path.Combine(Root, "Backups");
    internal static string Evidence => Path.Combine(Root, "Evidence");
    internal static string Profiles => Path.Combine(Root, "Profiles");
    internal static string Preferences => Path.Combine(Root, "Preferences", "preferences.json");
    internal static string LegacyPreferences => Path.Combine(AppContext.BaseDirectory, "Preferences", "preferences.json");
}
