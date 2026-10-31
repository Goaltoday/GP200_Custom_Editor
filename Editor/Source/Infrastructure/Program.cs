using System.Windows;
namespace Gp200.Replica;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var unknown=args.FirstOrDefault(a=>a.StartsWith("--",StringComparison.Ordinal)&&a is not ("--english" or "--spanish"));
            if(unknown!=null){Console.Error.WriteLine("Unsupported command-line option.");return 2;}
            UiText.Language = args.Contains("--spanish") ? "es" : args.Contains("--english") ? "en" : AppPreferences.Load().Language;
            var app = new Application();
            app.Run(new EditorWindow(args.Where(p => p.EndsWith(".prst", StringComparison.OrdinalIgnoreCase)).ToArray(), !AppPreferences.ProfileSelectionRequired));
            return 0;
        }
        catch (Exception e) { UiDialogs.Show(e.Message, "GP200 Replica"); return 1; }
    }
}
