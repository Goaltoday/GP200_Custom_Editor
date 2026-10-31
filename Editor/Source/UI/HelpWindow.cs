using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Gp200.Replica;

public sealed record AppPreferences
{
    public string Language {get;init;}="en";
    public string DeviceProfile {get;init;}="Stock";
    public bool NeedsProfileSelection {get;init;}
    public static bool ProfileSelectionRequired {get;private set;}
    public static string? LoadWarning {get;private set;}
    public int ScalePercent {get;init;}=100;
    public string SnapToneLibraryPath {get;init;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"GP200 Replica","SnapTone Library");
    public string ManualPath {get;init;}=Path.Combine(AppContext.BaseDirectory,"Documentation","GP200-Manual.pdf");
    public string AnalysisPath {get;init;}=Path.Combine(AppContext.BaseDirectory,"Documentation");
    public string FirmwarePath {get;init;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"GP200","firmware.bin");
    public static string FilePath=>AppPaths.Preferences;
    public static AppPreferences Load()
    {
        string source=File.Exists(FilePath)?FilePath:AppPaths.LegacyPreferences;
        if(!File.Exists(source))return new();
        try{
            var value=JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(source))??new();
            if(value.DeviceProfile is "Custom" or "Custom5") {ProfileSelectionRequired=true;LoadWarning="Custom5 is no longer supported. Select Stock or Custom in Settings > Device profile, then restart.";return value with{DeviceProfile="Stock",NeedsProfileSelection=true};}
            value=Validate(value);
            if(value.NeedsProfileSelection){ProfileSelectionRequired=true;LoadWarning="Select Stock or Custom in Settings > Device profile, then restart.";}
            if(source!=FilePath)value.Save(); // atomic copy; legacy file is kept
            return value;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){ProfileSelectionRequired=true;LoadWarning="Preferences could not be loaded: "+e.Message;return new(){NeedsProfileSelection=true};}
    }
    public static AppPreferences Validate(AppPreferences value)
    {
        if(value.Language is not ("es" or "en"))throw new ArgumentException(UiText.T("Unsupported language."));
        if(value.DeviceProfile is not ("Stock" or "MX42"))throw new ArgumentException("Unknown device profile.");
        if(value.ScalePercent is not (100 or 110 or 120))throw new ArgumentException(UiText.T("La escala debe ser 100, 110 o 120 %."));
        foreach(string path in new[]{value.ManualPath,value.AnalysisPath,value.FirmwarePath,value.SnapToneLibraryPath})
            if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||Uri.TryCreate(path,UriKind.Absolute,out var uri)&&(uri.Scheme is not "file"||uri.IsUnc))throw new ArgumentException(UiText.T("Las preferencias solo admiten rutas locales absolutas."));
        return value;
    }
    public void Save()
    {
        Validate(this);string folder=Path.GetDirectoryName(FilePath)!;Directory.CreateDirectory(folder);
        string temporary=Path.Combine(folder,$"preferences-{Guid.NewGuid():N}.tmp");
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,FilePath,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}

public sealed class HelpWindow:Window
{
    AppPreferences preferences;
    readonly TextBox report=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(14)};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    readonly WrapPanel commands=new();
    readonly ComboBox scale=new(){ItemsSource=new[]{"100 %","110 %","120 %"},MinWidth=100,Margin=new Thickness(4)};
    public event Action<double>? ScaleChanged;
    public HelpWindow()
    {
        EditorWindow.StyleDialog(this);
        preferences=AppPreferences.Load();Title=UiText.T("GP-200 · Ayuda y preferencias");Width=860;Height=640;MinWidth=700;MinHeight=500;
        var panel=new DockPanel{Margin=new Thickness(20)};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(new TextBlock{Text=UiText.T("Ayuda, preferencias e información"),FontSize=18,FontWeight=FontWeights.Bold});
        top.Children.Add(new TextBlock{Text=UiText.T("Manual local del equipo, documentación de la réplica e inspección de archivos de firmware."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});top.Children.Add(commands);
        Add(UiText.T("Abrir manual"),()=>OpenLocal(preferences.ManualPath));Add(UiText.T("Elegir manual…"),ChooseManual);Add(UiText.T("Informe de progreso"),ShowProgress);Add(UiText.T("Fuentes y evidencias"),ShowSources);
        Add(UiText.T("Inspeccionar firmware BIN…"),()=>InspectFirmware(true));Add(UiText.T("Ver firmware aportado"),()=>InspectFirmware(false));Add(UiText.T("Guardar informe…"),SaveReport);
        var settings=new WrapPanel{Margin=new Thickness(0,10,0,10)};top.Children.Add(settings);settings.Children.Add(new TextBlock{Text=UiText.T("Escala de interfaz:"),VerticalAlignment=VerticalAlignment.Center});settings.Children.Add(scale);scale.SelectedIndex=preferences.ScalePercent/10-10;
        scale.SelectionChanged+=(_,_)=>Run(()=>{preferences=(preferences with{ScalePercent=100+10*scale.SelectedIndex});preferences.Save();ScaleChanged?.Invoke(preferences.ScalePercent/100.0);status.Text=UiText.T("Escala guardada: ")+preferences.ScalePercent+UiText.T(" %.");});
        settings.Children.Add(new TextBlock{Text=UiText.T("Idioma:"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,4,0)});
        var language=new ComboBox{ItemsSource=new[]{"Español","English"},SelectedIndex=UiText.Language=="en"?1:0,MinWidth=120,Margin=new Thickness(4)};settings.Children.Add(language);
        language.SelectionChanged+=(_,_)=>Run(()=>{preferences=preferences with{Language=language.SelectedIndex==1?"en":"es"};preferences.Save();status.Text=UiText.T("Idioma guardado. Reinicia la aplicación para aplicarlo.");});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);bottom.Children.Add(status);panel.Children.Add(report);Content=panel;
        ShowSources();
    }
    void Add(string label,Action action){var button=new Button{Content=UiText.T(label),Padding=new Thickness(7,4,7,4),Margin=new Thickness(4)};button.Click+=(_,_)=>Run(action);commands.Children.Add(button);}
    void Run(Action action){try{action();}catch(Exception ex){status.Text=ex.Message;UiDialogs.Show(this,ex.Message,UiText.T("GP-200"),MessageBoxButton.OK,MessageBoxImage.Error);}}
    static void OpenLocal(string path)
    {
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))throw new FileNotFoundException(UiText.T("No se encuentra el archivo local."),path);
        if(!Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.T("El manual debe ser un archivo PDF local."));
        Process.Start(new ProcessStartInfo{FileName=path,UseShellExecute=true});
    }
    void ChooseManual()
    {
        var dialog=new OpenFileDialog{Filter=UiText.Filter("Manual PDF|*.pdf"),CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
        preferences=preferences with{ManualPath=dialog.FileName};preferences.Save();OpenLocal(dialog.FileName);status.Text=UiText.T("Manual local guardado en preferencias.");
    }
    void ShowProgress()
    {
        string path=Path.Combine(preferences.AnalysisPath,"PROGRESS.md");if(!File.Exists(path))throw new FileNotFoundException(UiText.T("No se encuentra el informe de progreso."),path);
        report.Text=File.ReadAllText(path);status.Text=UiText.T("Documento local: ")+path+UiText.T(". Las estimaciones pertenecen a la fecha del informe.");
    }
    void ShowSources()
    {
        string readme=Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName??AppContext.BaseDirectory,"README.md");
        var text=new StringBuilder(UiText.T("GP200 Replica\n\nFuente C#/WPF independiente basada en evidencias del editor oficial mediante REA 6.7.0 y Ghidra 12.1.4.\n\n"));
        text.AppendLine(UiText.T("Manual: ")+preferences.ManualPath).AppendLine(UiText.T("Informes y evidencias: ")+preferences.AnalysisPath).AppendLine(UiText.T("Preferencias: ")+AppPreferences.FilePath);
        text.AppendLine(UiText.T("\\nLa conversión/importación de SnapTone está excluida. Este panel inspecciona firmware como archivo local y no incluye instalación ni envío al equipo. El marcador de versión del archivo no demuestra la versión ejecutada por la pedalera."));
        if(File.Exists(readme))text.AppendLine("\n"+UiText.T("Código fuente e instrucciones: ")+readme);report.Text=text.ToString();status.Text=UiText.T("Información local. No se han abierto puertos ni consultado servicios externos.");
    }
    void InspectFirmware(bool choose)
    {
        string path=preferences.FirmwarePath;
        if(choose){var dialog=new OpenFileDialog{Filter=UiText.Filter("Firmware BIN|*.bin|Todos los archivos|*.*"),CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;path=dialog.FileName;}
        var result=FirmwareInspector.InspectFile(path);
        preferences=preferences with{FirmwarePath=path};preferences.Save();
        var text=new StringBuilder(UiText.T("Inspección local de contenedor firmware\n\n"));text.AppendLine(UiText.T("Archivo: ")+path).AppendLine(UiText.T("Modelo: ")+result.Model).AppendLine(UiText.T("Tamaño: ")+result.Size+UiText.T(" bytes")).AppendLine(UiText.T("Cuerpo: ")+result.BodySize+UiText.T(" bytes · base48")).AppendLine(UiText.T("Checksum cuerpo: ")+result.Checksum+UiText.T(" · correcto")).AppendLine(UiText.T("SHA-256: ")+result.Sha256).AppendLine(UiText.T("Marcador: ")+result.VersionMarker+UiText.T(" · hex ")+result.VersionMarkerHex).AppendLine(UiText.T("Formato extendido EX: ")+result.ExtendedFlag).AppendLine(UiText.T("\nCuatro regiones:"));
        foreach(var region in result.Regions)text.AppendLine(UiText.T($"\nPar {region.DiskPair} → índice interno {region.InternalIndex}\nOffset {region.Offset} · tamaño {region.Length} · fin {region.End}\nSHA-256 {region.Sha256}"));
        text.AppendLine(UiText.T("\nConfirmado: firma FRMW, modelo exacto, tamaño, checksum de cuerpo, límites y ausencia de solapes.\nPendiente: arquitectura y comportamiento de cada componente. Esta inspección no ejecuta código ni comprueba el firmware instalado en la GP-200."));report.Text=text.ToString();status.Text=UiText.T("Inspección finalizada. Ningún dato enviado al equipo.");
    }
    void SaveReport(){var dialog=new SaveFileDialog{Filter=UiText.Filter("Informe de texto|*.txt"),FileName="GP200-informe-local.txt"};if(dialog.ShowDialog(this)==true){File.WriteAllText(dialog.FileName,report.Text);status.Text=UiText.T("Informe guardado: ")+dialog.FileName;}}
}
